using System.Text.Json;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Applies an authenticated delivery report to exactly one durable attempt. Idempotent by provider event ID
/// (inbox + unique delivery event), serialized with the dispatch worker through the alert mutation lock, and
/// unable to regress a terminal attempt. It records opaque identifiers and closed vocabulary only.
/// </summary>
public sealed class ProviderDeliveryReportService(CriticalAlertsDbContext db, TimeProvider time) : IProviderDeliveryReportService
{
    public const string AcsSmsHandler = "acs-sms-delivery-report";
    public const string SmsDeliveryFailed = "sms-delivery-failed";

    /// <summary>Technical bound for redelivering a report that may race the send commit; not a clinical threshold.</summary>
    public static readonly TimeSpan UnmatchedDeferralWindow = TimeSpan.FromMinutes(10);

    public async Task<ProviderDeliveryReportOutcome> ApplyAsync(
        string provider,
        ProviderDeliveryReport report,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (provider != AzureCommunicationServicesSmsChannel.Provider)
            throw new DispatchValidationException("provider-invalid", "The delivery report provider is not supported.");

        var candidates = await db.DeliveryAttempts.AsNoTracking()
            .Where(item => item.Provider == provider && item.ProviderReference == report.ProviderMessageId)
            .Select(item => new { item.Id, item.OrganizationId, item.AlertId })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (candidates.Length != 1)
        {
            // ACS can publish a report before the worker transaction that stores the message ID commits.
            // Store nothing and let the sender redeliver while that race is plausible; later, drop it as foreign.
            return candidates.Length == 0 && report.OccurredAtUtc > now - UnmatchedDeferralWindow
                ? ProviderDeliveryReportOutcome.Deferred
                : ProviderDeliveryReportOutcome.Unmatched;
        }

        var candidate = candidates[0];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AlertMutationLock.AcquireAsync(db, candidate.OrganizationId, candidate.AlertId, cancellationToken);
        var attempt = await db.DeliveryAttempts
            .FromSqlInterpolated($"SELECT * FROM delivery_attempts WHERE id = {candidate.Id.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var providerEventId = $"acs-eg:{report.ProviderEventId}";
        if (await db.InboxMessages.AnyAsync(item => item.ExternalMessageId == report.ProviderEventId && item.Handler == AcsSmsHandler, cancellationToken)
            || await db.DeliveryEvents.AnyAsync(item => item.OrganizationId == attempt.OrganizationId && item.ProviderEventId == providerEventId, cancellationToken))
        {
            return ProviderDeliveryReportOutcome.Duplicate;
        }

        var auditCorrelation = AuditSafety.IsSafeCorrelationId(correlationId) ? correlationId : Guid.NewGuid().ToString("N");
        if (!string.Equals(report.Tag, AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey), StringComparison.Ordinal))
        {
            db.InboxMessages.Add(InboxMessage.Create(InboxMessageId.New(), attempt.OrganizationId, report.ProviderEventId,
                AcsSmsHandler, "rejected-tag-mismatch", now));
            Audit(attempt, "failed", auditCorrelation, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ProviderDeliveryReportOutcome.TagMismatch;
        }

        var before = attempt.Status;
        if (report.Status == ProviderDeliveryReportStatus.Delivered)
            attempt.MarkDelivered(report.OccurredAtUtc);
        else
            attempt.MarkFailed(SmsDeliveryFailed, report.OccurredAtUtc);
        var changed = attempt.Status != before;

        db.DeliveryEvents.Add(DeliveryEvent.Create(
            DeliveryEventId.New(),
            attempt.OrganizationId,
            attempt.Id,
            report.Status == ProviderDeliveryReportStatus.Delivered ? "delivered" : "failed",
            providerEventId,
            now,
            $"acs-sms-report:{report.Status}",
            report.OccurredAtUtc));
        db.InboxMessages.Add(InboxMessage.Create(InboxMessageId.New(), attempt.OrganizationId, report.ProviderEventId,
            AcsSmsHandler, changed ? "applied" : "no-state-change", now));
        Audit(attempt, "succeeded", auditCorrelation, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed ? ProviderDeliveryReportOutcome.Applied : ProviderDeliveryReportOutcome.NoStateChange;
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
