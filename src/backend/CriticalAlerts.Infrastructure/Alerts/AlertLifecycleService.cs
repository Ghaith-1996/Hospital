using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Alerts;

public sealed class AlertLifecycleService(
    CriticalAlertsDbContext db,
    TimeProvider time) : IAlertLifecycleService
{
    public Task<AlertLifecycleResult?> ResolveAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        AlertLifecycleActionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            organizationId,
            actorUserId,
            correlationId,
            alertId,
            request,
            idempotencyKey,
            operation: "alert-resolve",
            expectedState: AlertState.Resolved,
            cancellationToken);

    public Task<AlertLifecycleResult?> CancelAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        AlertLifecycleActionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
        => ExecuteAsync(
            organizationId,
            actorUserId,
            correlationId,
            alertId,
            request,
            idempotencyKey,
            operation: "alert-cancel",
            expectedState: AlertState.Cancelled,
            cancellationToken);

    private async Task<AlertLifecycleResult?> ExecuteAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        AlertLifecycleActionRequest request,
        string? idempotencyKey,
        string operation,
        AlertState expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = RequireIdempotencyKey(idempotencyKey);
        var requestHash = ComputeRequestHash(operation, organizationId, alertId, request.ExpectedVersion);
        var now = RequireUtc(time.GetUtcNow());
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AlertMutationLock.AcquireAsync(db, organizationId, alertId, cancellationToken);
            var existing = await FindIdempotencyAsync(organizationId, operation, key, requestHash, cancellationToken);
            if (existing is not null)
            {
                return DecodeResult(existing, expectedState, replayed: true);
            }

            var alert = await db.Alerts
                .Include(candidate => candidate.StateTransitions)
                .SingleOrDefaultAsync(
                    candidate => candidate.OrganizationId == organizationId && candidate.Id == alertId,
                    cancellationToken);
            if (alert is null)
            {
                return null;
            }

            if (alert.ConfirmedDraftVersion?.Value != request.ExpectedVersion)
            {
                throw Conflict("alert-version-stale", "The alert version changed. Reload the live status before acting.");
            }

            if (alert.State != AlertState.Active)
            {
                throw Conflict("alert-state-conflict", "Only an active simulation alert can be resolved or cancelled.");
            }

            if (expectedState == AlertState.Resolved)
            {
                var hasActiveResponsibility = await db.ResponsibilityAssignments.AnyAsync(assignment =>
                    assignment.OrganizationId == organizationId
                    && assignment.AlertId == alertId
                    && assignment.AlertVersion == alert.ConfirmedDraftVersion!.Value
                    && assignment.ReleasedAtUtc == null,
                    cancellationToken);
                if (!hasActiveResponsibility)
                {
                    throw Conflict(
                        "responsibility-required",
                        "Resolution requires an active responsibility assignment for the confirmed alert version.");
                }

                alert.Resolve(actorUserId, now, correlationId);
            }
            else
            {
                alert.Cancel(actorUserId, now, correlationId);
            }

            var result = new AlertLifecycleResult(alert.Id.Value, request.ExpectedVersion, expectedState.ToString(), Replayed: false);
            var idempotency = IdempotencyRecord.Start(
                IdempotencyRecordId.New(),
                organizationId,
                operation,
                key,
                requestHash,
                now);
            idempotency.Complete(EncodeResult(result));
            db.IdempotencyRecords.Add(idempotency);
            db.AuditEvents.Add(AuditEvent.Record(
                AuditEventId.New(),
                organizationId,
                "user",
                actorUserId,
                $"alert.{expectedState.ToString().ToLowerInvariant()}",
                "alert",
                alertId.Value,
                "succeeded",
                correlationId,
                JsonSerializer.Serialize(new
                {
                    simulationOnly = true,
                    alertVersion = request.ExpectedVersion,
                    action = expectedState.ToString(),
                }),
                now));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ResolveConcurrentAsync(
                organizationId,
                operation,
                key,
                requestHash,
                expectedState,
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await ResolveConcurrentAsync(
                organizationId,
                operation,
                key,
                requestHash,
                expectedState,
                cancellationToken);
        }
    }

    private async Task<IdempotencyRecord?> FindIdempotencyAsync(
        OrganizationId organizationId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(record =>
            record.OrganizationId == organizationId
            && record.OperationType == operation
            && record.IdempotencyKey == key,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (!FixedEquals(existing.RequestHash, requestHash))
        {
            throw Conflict("idempotency-conflict", "The idempotency key was already used for a different lifecycle request.");
        }

        if (existing.Status != IdempotencyProcessingStatus.Completed)
        {
            throw Conflict("lifecycle-in-progress", "A lifecycle action with this idempotency key is already in progress.");
        }

        return existing;
    }

    private async Task<AlertLifecycleResult?> ResolveConcurrentAsync(
        OrganizationId organizationId,
        string operation,
        string key,
        string requestHash,
        AlertState expectedState,
        CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.OrganizationId == organizationId
            && record.OperationType == operation
            && record.IdempotencyKey == key,
            cancellationToken);
        if (existing is null)
        {
            throw Conflict("lifecycle-conflict", "The lifecycle action could not be committed. Reload the live status and retry safely.");
        }

        if (!FixedEquals(existing.RequestHash, requestHash))
        {
            throw Conflict("idempotency-conflict", "The idempotency key was already used for a different lifecycle request.");
        }

        if (existing.Status != IdempotencyProcessingStatus.Completed)
        {
            throw Conflict("lifecycle-in-progress", "A lifecycle action with this idempotency key is already in progress.");
        }

        return DecodeResult(existing, expectedState, replayed: true);
    }

    private static string RequireIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AlertLifecycleValidationException(
                "idempotency-key-required",
                "An Idempotency-Key header is required for alert lifecycle actions.");
        }

        var normalized = value.Trim();
        if (normalized.Length > 128 || normalized.Any(character => character < 0x21 || character > 0x7E))
        {
            throw new AlertLifecycleValidationException(
                "idempotency-key-invalid",
                "The Idempotency-Key header must contain 1 to 128 visible ASCII characters.");
        }

        return normalized;
    }

    private static string ComputeRequestHash(string operation, OrganizationId organizationId, AlertId alertId, int expectedVersion)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{operation}|{organizationId.Value:D}|{alertId.Value:D}|{expectedVersion}")));

    private static string EncodeResult(AlertLifecycleResult result)
        => $"{result.AlertId:D}|{result.ConfirmedVersion}|{result.State}";

    private static AlertLifecycleResult DecodeResult(IdempotencyRecord record, AlertState expectedState, bool replayed)
    {
        var parts = record.ResultReference.Split('|', StringSplitOptions.None);
        if (parts.Length != 3
            || !Guid.TryParse(parts[0], out var alertId)
            || !int.TryParse(parts[1], out var version)
            || !string.Equals(parts[2], expectedState.ToString(), StringComparison.Ordinal))
        {
            throw new AlertLifecycleValidationException(
                "lifecycle-conflict",
                "The stored lifecycle result is invalid. Reload the live status and retry safely.");
        }

        return new AlertLifecycleResult(alertId, version, parts[2], replayed);
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new InvalidOperationException("The lifecycle clock must be UTC.");

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static AlertLifecycleValidationException Conflict(string code, string message) => new(code, message);
}
