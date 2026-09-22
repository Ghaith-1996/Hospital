using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class EscalationProcessor(CriticalAlertsDbContext db)
{
    public async Task<bool> ProcessNextAsync(string owner, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        db.ChangeTracker.Clear();
        var scheduled = await ScheduleNextAsync(cancellationToken);
        var id = await ClaimAsync(owner, cancellationToken);
        if (id == Guid.Empty) return scheduled;

        // Claim commits before taking the shared alert lock: all mutations lock alert, then run.
        db.ChangeTracker.Clear();
        var claimed = await db.EscalationRuns.AsNoTracking().SingleAsync(row => row.Id == new EscalationRunId(id), cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AlertMutationLock.AcquireAsync(db, claimed.OrganizationId, claimed.AlertId, cancellationToken);
        var run = await db.EscalationRuns.FromSqlInterpolated($"SELECT * FROM escalation_runs WHERE id = {id} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var now = await EscalationPlanReview.DatabaseNowAsync(db, cancellationToken);
        if (run.LeaseOwner != owner || run.LeaseExpiresAtUtc <= now) return true;
        var alert = await db.Alerts.SingleAsync(row => row.OrganizationId == run.OrganizationId && row.Id == run.AlertId, cancellationToken);
        var responsibility = await db.ResponsibilityAssignments.AnyAsync(row => row.OrganizationId == run.OrganizationId
            && row.AlertId == run.AlertId && row.AlertVersion == run.AlertVersion && row.ReleasedAtUtc == null, cancellationToken);
        var negatives = await db.RecipientResponses.CountAsync(row => row.OrganizationId == run.OrganizationId
            && row.AlertId == run.AlertId && row.AlertVersion == run.AlertVersion
            && (row.ResponseType == RecipientResponseType.Declined || row.ResponseType == RecipientResponseType.Unavailable), cancellationToken);
        var decision = run.Evaluate(alert.State, responsibility, negatives, now);
        if (alert.ConfirmedDraftVersion != run.AlertVersion || alert.DraftVersion != run.AlertVersion
            || alert.State == AlertState.Failed)
            decision = EscalationEventKind.ProcessingFailed;

        if (decision == EscalationEventKind.StepDue)
        {
            var approval = await db.ConfirmedEscalationPlans.SingleAsync(row => row.OrganizationId == run.OrganizationId
                && row.AlertId == run.AlertId && row.AlertVersion == run.AlertVersion, cancellationToken);
            EscalationPlanView? plan = null;
            try { plan = ReadPlan(approval); }
            catch (JsonException) { decision = EscalationEventKind.ProcessingFailed; }
            catch (DomainException) { decision = EscalationEventKind.ProcessingFailed; }
            if (plan is not null) await ActivateAsync(alert, run, approval, plan, negatives, now, cancellationToken);
        }
        if (decision is not null && decision != EscalationEventKind.StepDue)
        {
            run.Stop(decision.Value, now);
            db.EscalationEvents.Add(run.Record(decision.Value, now));
        }
        run.ReleaseLease(owner, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ScheduleNextAsync(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var id = await db.Database.SqlQuery<Guid>($"""
            SELECT a.id AS "Value" FROM alerts a
            JOIN confirmed_escalation_plans p ON p.organization_id = a.organization_id
                AND p.alert_id = a.id AND p.alert_version = a.confirmed_draft_version
            WHERE a.state = 'Active' AND a.draft_version = a.confirmed_draft_version
                AND NOT EXISTS (SELECT 1 FROM escalation_runs r WHERE r.organization_id = a.organization_id
                    AND r.alert_id = a.id AND r.alert_version = a.confirmed_draft_version)
            ORDER BY a.id FOR UPDATE OF a SKIP LOCKED LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (id == Guid.Empty) return false;
        // The alert lock also serializes creation with response and lifecycle commands.
        var approval = await db.ConfirmedEscalationPlans.SingleAsync(row => row.AlertId == new AlertId(id), ct);
        var now = await EscalationPlanReview.DatabaseNowAsync(db, ct);
        EscalationPlanView? plan = null;
        try { plan = ReadPlan(approval); }
        catch (JsonException) { }
        catch (DomainException) { }
        var run = EscalationRun.Schedule(EscalationRunId.New(), approval.OrganizationId, approval.AlertId,
            approval.PolicyId, approval.PolicyVersion, now.AddSeconds(plan?.Steps[0].DelaySeconds ?? 0), now, approval.AlertVersion);
        db.EscalationRuns.Add(run);
        db.EscalationEvents.Add(run.Record(EscalationEventKind.Scheduled, now));
        if (plan is null)
        {
            run.Stop(EscalationEventKind.ProcessingFailed, now);
            db.EscalationEvents.Add(run.Record(EscalationEventKind.ProcessingFailed, now));
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<Guid> ClaimAsync(string owner, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var id = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM escalation_runs
            WHERE alert_version IS NOT NULL AND state IN ('Scheduled', 'Paused', 'Exhausted')
                AND next_check_at_utc <= clock_timestamp()
                AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= clock_timestamp())
            ORDER BY next_check_at_utc, id FOR UPDATE SKIP LOCKED LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (id == Guid.Empty) return id;
        var run = await db.EscalationRuns.SingleAsync(row => row.Id == new EscalationRunId(id), ct);
        var now = await EscalationPlanReview.DatabaseNowAsync(db, ct);
        if (!run.TryAcquireLease(owner, now, TimeSpan.FromSeconds(30))) return Guid.Empty;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return id;
    }

    private async Task ActivateAsync(Alert alert, EscalationRun run, ConfirmedEscalationPlan approval,
        EscalationPlanView plan, int negatives, DateTimeOffset now, CancellationToken ct)
    {
        var step = plan.Steps.SingleOrDefault(row => row.SequenceNumber == run.CurrentStep);
        if (step is null)
        {
            run.Stop(EscalationEventKind.ProcessingFailed, now);
            db.EscalationEvents.Add(run.Record(EscalationEventKind.ProcessingFailed, now));
            return;
        }
        db.EscalationEvents.Add(run.Record(EscalationEventKind.StepDue, now));
        var existing = await db.AlertRecipientSelections.Where(row => row.OrganizationId == run.OrganizationId
            && row.AlertId == run.AlertId && row.AlertVersion == run.AlertVersion).ToListAsync(ct);
        var selections = new List<AlertRecipientSelection>();
        foreach (var recipient in step.Recipients)
        {
            var practitionerId = new PractitionerId(recipient.PractitionerId);
            var channel = Enum.Parse<NotificationChannel>(recipient.Channel);
            if (existing.Concat(selections).Any(row => row.PractitionerId == practitionerId && row.Channel == channel)) continue;
            var selection = new AlertRecipientSelection(AlertRecipientSelectionId.New(), run.OrganizationId, run.AlertId,
                approval.AlertVersion, practitionerId,
                recipient.PractitionerRoleId is Guid role ? new PractitionerRoleId(role) : null,
                channel, approval.ConfirmedByUserId, now, recipient.DirectoryRevision,
                recipient.DirectorySourceUpdatedAtUtc, recipient.OnCallSnapshot, RecipientSelectionSource.EscalationPolicy);
            selections.Add(selection);
            db.EscalationEvents.Add(run.Record(EscalationEventKind.RecipientActivated, now, selection.Id.Value));
        }
        db.AlertRecipientSelections.AddRange(selections);
        if (selections.Count > 0)
        {
            db.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageId.New(), run.OrganizationId, "EscalationDispatchRequested",
                alert.Id.Value, JsonSerializer.Serialize(new { alertId = alert.Id.Value, draftVersion = approval.AlertVersion.Value,
                    recipientSelectionIds = selections.Select(row => row.Id.Value).ToArray() }),
                $"escalation:{run.Id.Value:N}:step:{run.CurrentStep}", now));
            db.EscalationEvents.Add(run.Record(EscalationEventKind.DispatchQueued, now));
        }
        var next = plan.Steps.SingleOrDefault(row => row.SequenceNumber == run.CurrentStep + 1);
        run.Advance(next is null ? null : TimeSpan.FromSeconds(next.DelaySeconds), negatives, now);
        if (run.State == EscalationRunState.Exhausted) db.EscalationEvents.Add(run.Record(EscalationEventKind.Exhausted, now));
    }

    private static EscalationPlanView ReadPlan(ConfirmedEscalationPlan approval)
    {
        var plan = JsonSerializer.Deserialize<EscalationPlanView>(approval.SnapshotJson);
        if (plan is null || plan.PolicyId != approval.PolicyId.Value || plan.PolicyVersion != approval.PolicyVersion
            || plan.Revision != approval.Revision || plan.Steps is null || plan.Steps.Count == 0
            || plan.Steps.Where((step, index) => step.SequenceNumber != index + 1 || step.DelaySeconds is < 0 or > 86400
                || step.Recipients is null || step.Recipients.Any(row => row.PractitionerId == Guid.Empty
                    || row.Channel != "SecureMessage" || string.IsNullOrWhiteSpace(row.DirectoryRevision))).Any())
            throw new DomainException("The approved DEMO escalation plan is invalid.");
        return plan;
    }
}
