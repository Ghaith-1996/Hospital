using System.Text.Json;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Applies authenticated, validated voice call events idempotently: one transaction per event, the alert mutation lock,
/// inbox and delivery-event deduplication, per-attempt tag binding and the domain's no-regression transitions. An event
/// for a call ID that is not committed yet, but whose tag matches a committed send, is held for the worker instead of
/// relying on the provider to redeliver it. Only opaque identifiers and closed vocabulary are recorded.
/// </summary>
public sealed class ProviderCallEventService(CriticalAlertsDbContext db, TimeProvider time) : IProviderCallEventService
{
    public static string Handler(string provider) => $"voice:{provider}";

    public static string ProviderEventId(string provider, string eventId) => $"voice:{provider}:{eventId}";

    public async Task<ProviderCallEventOutcome> ApplyAsync(
        string provider,
        VoiceCallEvent callEvent,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!VoiceDispatchOptions.IsValidProviderName(provider))
            throw new DispatchValidationException("provider-invalid", "The call event provider is not supported.");
        var kind = VoiceCallEventMapping.Kind(callEvent.Kind);
        var endReason = callEvent.EndReason is { } reason ? VoiceCallEventMapping.Reason(reason) : null;
        var (eventType, status, category) = VoiceCallEventMapping.Map(kind, endReason);

        // Every event re-reads its attempt after taking the alert lock; never reuse a copy an earlier event left tracked.
        db.ChangeTracker.Clear();
        var now = time.GetUtcNow();
        var candidates = await db.DeliveryAttempts.AsNoTracking()
            .Where(item => item.Provider == provider && item.ProviderReference == callEvent.CallId)
            .Select(item => new { item.Id, item.OrganizationId, item.AlertId })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (candidates.Length == 0) return await HoldIfSendIsUncommittedAsync(provider, callEvent, kind, endReason, now, cancellationToken);
        if (candidates.Length != 1) return ProviderCallEventOutcome.Unmatched;

        var candidate = candidates[0];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AlertMutationLock.AcquireAsync(db, candidate.OrganizationId, candidate.AlertId, cancellationToken);
        var attempt = await db.DeliveryAttempts
            .FromSqlInterpolated($"SELECT * FROM delivery_attempts WHERE id = {candidate.Id.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var handler = Handler(provider);
        var providerEventId = ProviderEventId(provider, callEvent.EventId);
        if (await db.InboxMessages.AnyAsync(item => item.ExternalMessageId == callEvent.EventId && item.Handler == handler, cancellationToken)
            || await db.DeliveryEvents.AnyAsync(item => item.OrganizationId == attempt.OrganizationId && item.ProviderEventId == providerEventId, cancellationToken))
        {
            return ProviderCallEventOutcome.Duplicate;
        }

        var auditCorrelation = AuditSafety.IsSafeCorrelationId(correlationId) ? correlationId : Guid.NewGuid().ToString("N");
        if (!string.Equals(callEvent.Tag, ProviderVoiceChannel.CreateTag(provider, attempt.IdempotencyKey), StringComparison.Ordinal))
        {
            db.InboxMessages.Add(InboxMessage.Create(InboxMessageId.New(), attempt.OrganizationId, callEvent.EventId,
                handler, "rejected-tag-mismatch", now));
            Audit(attempt, "failed", auditCorrelation, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProviderCallEventOutcome.TagMismatch;
        }

        var before = attempt.Status;
        if (status == DeliveryAttemptStatus.Delivered)
            attempt.MarkDelivered(callEvent.OccurredAtUtc);
        else if (status == DeliveryAttemptStatus.Failed)
            attempt.MarkFailed(category!, callEvent.OccurredAtUtc);
        var changed = attempt.Status != before;

        db.DeliveryEvents.Add(DeliveryEvent.Create(
            DeliveryEventId.New(),
            attempt.OrganizationId,
            attempt.Id,
            eventType,
            providerEventId,
            now,
            VoiceCallEventMapping.Metadata(kind, endReason),
            callEvent.OccurredAtUtc));
        db.InboxMessages.Add(InboxMessage.Create(InboxMessageId.New(), attempt.OrganizationId, callEvent.EventId,
            handler, changed ? "applied" : "no-state-change", now));
        Audit(attempt, "succeeded", auditCorrelation, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed ? ProviderCallEventOutcome.Applied : ProviderCallEventOutcome.NoStateChange;
    }

    /// <summary>
    /// No committed attempt has this call ID. If the tag binds the event to a committed send whose attempt is not committed
    /// (or still awaits a same-key replay), hold it for the worker. It takes no alert lock: the worker may hold that lock
    /// while waiting for the provider reply that this very callback raced.
    /// </summary>
    private async Task<ProviderCallEventOutcome> HoldIfSendIsUncommittedAsync(
        string provider,
        VoiceCallEvent callEvent,
        string kind,
        string? endReason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (callEvent.Tag is null) return ProviderCallEventOutcome.Unmatched;
        var send = await db.ProviderSendRecords.AsNoTracking()
            .Where(row => row.Provider == provider && row.CallbackTag == callEvent.Tag)
            .Select(row => new { row.OrganizationId, row.AttemptIdempotencyKey })
            .SingleOrDefaultAsync(cancellationToken);
        if (send is null) return ProviderCallEventOutcome.Unmatched;

        var committed = await db.DeliveryAttempts.AsNoTracking()
            .Where(item => item.OrganizationId == send.OrganizationId && item.Provider == provider
                && item.IdempotencyKey == send.AttemptIdempotencyKey)
            .Select(item => new { item.Status, item.ProviderReference })
            .FirstOrDefaultAsync(cancellationToken);
        // A committed attempt that already has a different call ID, or is terminal, cannot be this call.
        if (committed is not null && (committed.Status != DeliveryAttemptStatus.Requested || committed.ProviderReference.Length > 0))
            return ProviderCallEventOutcome.Unmatched;

        if (await db.PendingProviderCallEvents.AnyAsync(row => row.Provider == provider && row.ExternalEventId == callEvent.EventId, cancellationToken))
            return ProviderCallEventOutcome.Duplicate;
        db.PendingProviderCallEvents.Add(PendingProviderCallEvent.Create(send.OrganizationId, provider, callEvent.Tag,
            callEvent.EventId, callEvent.CallId, kind, endReason, callEvent.OccurredAtUtc, now));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ProviderCallEventOutcome.Duplicate;
        }

        return ProviderCallEventOutcome.Pending;
    }

    private void Audit(DeliveryAttempt attempt, string outcome, string correlationId, DateTimeOffset now)
        => db.AuditEvents.Add(AuditEvent.Record(
            AuditEventId.New(),
            attempt.OrganizationId,
            "provider-webhook",
            null,
            "dispatch.delivery-event",
            "delivery-attempt",
            attempt.Id.Value,
            outcome,
            correlationId,
            JsonSerializer.Serialize(new { channel = attempt.Channel.ToString(), attemptNumber = attempt.AttemptNumber }),
            now));
}
