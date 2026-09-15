using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Escalation;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed class EscalationOverrideService(CriticalAlertsDbContext db) : IEscalationOverrideService
{
    private const string Operation = "escalation-override";

    public Task<EscalationOverrideResult?> PauseAsync(OrganizationId organizationId, UserId actorUserId, string correlationId,
        AlertId alertId, EscalationOverrideRequest request, string? idempotencyKey, CancellationToken cancellationToken)
        => ExecuteAsync(organizationId, actorUserId, correlationId, alertId, request, idempotencyKey, true, cancellationToken);

    public Task<EscalationOverrideResult?> ResumeAsync(OrganizationId organizationId, UserId actorUserId, string correlationId,
        AlertId alertId, EscalationOverrideRequest request, string? idempotencyKey, CancellationToken cancellationToken)
        => ExecuteAsync(organizationId, actorUserId, correlationId, alertId, request, idempotencyKey, false, cancellationToken);

    private async Task<EscalationOverrideResult?> ExecuteAsync(OrganizationId organizationId, UserId actorUserId, string correlationId,
        AlertId alertId, EscalationOverrideRequest request, string? idempotencyKey, bool pause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RequireKey(idempotencyKey);
        var reason = request.ReasonCode switch
        {
            nameof(EscalationOverrideReason.OperatorReview) when pause => EscalationOverrideReason.OperatorReview,
            nameof(EscalationOverrideReason.ManualCoordination) when pause => EscalationOverrideReason.ManualCoordination,
            nameof(EscalationOverrideReason.ReadyToResume) when !pause => EscalationOverrideReason.ReadyToResume,
            _ => throw new EscalationOverrideValidationException("escalation-reason-invalid", "An allowlisted DEMO reason code is required."),
        };
        if (request.ExpectedVersion < 1)
            throw new EscalationOverrideValidationException("alert-version-invalid", "An exact confirmed version is required.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Operation}|{pause}|{organizationId.Value:D}|{actorUserId.Value:D}|{alertId.Value:D}|{request.ExpectedVersion}|{reason}")));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AlertMutationLock.AcquireAsync(db, organizationId, alertId, cancellationToken);
            var replay = await ReplayAsync(organizationId, key, hash, cancellationToken);
            if (replay is not null) return replay;
            var alert = await db.Alerts.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == alertId, cancellationToken);
            if (alert is null) return null;
            await db.Entry(alert).ReloadAsync(cancellationToken);
            if (alert.ConfirmedDraftVersion?.Value != request.ExpectedVersion)
                throw Conflict("alert-version-stale");
            if (!alert.AutomaticEscalationEligible || alert.State != AlertState.Active)
                throw Conflict("escalation-state-conflict");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM escalation_runs WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value} AND alert_version = {request.ExpectedVersion} FOR UPDATE", cancellationToken);
            var run = await db.EscalationRuns.SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.AlertId == alertId
                && r.AlertVersion == new AlertDraftVersion(request.ExpectedVersion), cancellationToken);
            if (run is null) throw Conflict("escalation-state-conflict");
            await db.Entry(run).ReloadAsync(cancellationToken);
            var plan = await db.AlertEscalationPlans.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == run.PlanId, cancellationToken);
            if (plan is null || !EscalationRunRepository.Matches(alert, plan) || run.PlanRevision != plan.Revision
                || run.PolicyId != plan.EscalationPolicyId || run.PolicyVersion != plan.EscalationPolicyVersion
                || !DemoEscalationSemantics.IsSupported(plan.Definition)
                || (pause ? run.State is not (EscalationRunState.Scheduled or EscalationRunState.Running) : run.State != EscalationRunState.Paused)
                || await db.ResponsibilityAssignments.AnyAsync(a => a.OrganizationId == organizationId && a.AlertId == alertId
                    && a.AlertVersion == run.AlertVersion && a.ReleasedAtUtc == null, cancellationToken))
                throw Conflict("escalation-state-conflict");
            var now = await AlertMutationLock.TryGetMutationTimeAsync(db, organizationId, alertId, cancellationToken)
                ?? throw Conflict("escalation-clock-conflict");
            if (pause) run.Pause(actorUserId, now);
            else run.Resume(actorUserId, now);
            // Hash a non-GUID safe HTTP correlation into a stable opaque UUID. Audit and timeline share the same value.
            var correlation = Guid.TryParse(correlationId, out var parsed) && parsed != Guid.Empty ? parsed
                : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(correlationId)).AsSpan(0, 16));
            var entry = EscalationEvent.Record(run, pause ? EscalationEventType.Paused : EscalationEventType.Resumed,
                run.CurrentStep, actorUserId, correlation, now, overrideReason: reason);
            db.EscalationEvents.Add(entry);
            db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), organizationId, "user", actorUserId,
                pause ? "escalation.paused" : "escalation.resumed", "alert", alertId.Value, "succeeded", correlation.ToString("D"),
                JsonSerializer.Serialize(new { simulationOnly = true, alertVersion = request.ExpectedVersion, escalationRunId = run.Id.Value, reasonCode = reason.ToString() }), now));
            var retained = IdempotencyRecord.Start(IdempotencyRecordId.New(), organizationId, Operation, key, hash, now);
            retained.Complete(entry.Id.Value.ToString("N"));
            db.IdempotencyRecords.Add(retained);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(entry, false);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ReplayAsync(organizationId, key, hash, cancellationToken) ?? throw Conflict("escalation-conflict");
        }
    }

    private async Task<EscalationOverrideResult?> ReplayAsync(OrganizationId organizationId, string key, string hash, CancellationToken cancellationToken)
    {
        var retained = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.OrganizationId == organizationId
            && r.OperationType == Operation && r.IdempotencyKey == key, cancellationToken);
        if (retained is null) return null;
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(retained.RequestHash), Encoding.UTF8.GetBytes(hash)))
            throw Conflict("idempotency-conflict");
        if (retained.Status != IdempotencyProcessingStatus.Completed || !Guid.TryParse(retained.ResultReference, out var eventId))
            throw Conflict("escalation-conflict");
        var entry = await db.EscalationEvents.AsNoTracking().SingleOrDefaultAsync(e => e.OrganizationId == organizationId && e.Id == new EscalationEventId(eventId), cancellationToken);
        if (entry?.OverrideReason is null || entry.EventType is not (EscalationEventType.Paused or EscalationEventType.Resumed))
            throw Conflict("escalation-conflict");
        return Result(entry, true);
    }

    private static EscalationOverrideResult Result(EscalationEvent entry, bool replayed)
        => new(entry.AlertId.Value, entry.AlertVersion.Value, entry.RunId.Value,
            entry.EventType == EscalationEventType.Paused ? "Paused" : "Scheduled", entry.OverrideReason!.Value.ToString(), entry.OccurredAtUtc, replayed);

    private static string RequireKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new EscalationOverrideValidationException("idempotency-key-required", "An Idempotency-Key header is required.");
        key = key.Trim();
        if (key.Length > 100 || key.Any(c => c < 0x21 || c > 0x7e))
            throw new EscalationOverrideValidationException("idempotency-key-invalid", "The Idempotency-Key header must contain 1 to 100 visible ASCII characters.");
        return key;
    }

    private static EscalationOverrideValidationException Conflict(string code)
        => new(code, "The escalation command conflicts with saved state. Reload and retry safely.", true);
}
