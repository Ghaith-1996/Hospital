using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Responses;

public sealed class AlertLiveQueryService(
    CriticalAlertsDbContext db,
    TimeProvider time) : IAlertLiveQueryService
{
    public async Task<AlertLiveView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var alert = await db.Alerts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Id == alertId
                && item.ConfirmedDraftVersion != null,
                cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var version = alert.ConfirmedDraftVersion!.Value;
        var selections = await db.AlertRecipientSelections
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version)
            .ToArrayAsync(cancellationToken);
        var practitionerIds = selections.Select(item => item.PractitionerId).Distinct().ToArray();
        var practitioners = await db.Practitioners
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && practitionerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var selectionIds = selections.Select(item => item.Id).ToArray();
        var attempts = await db.DeliveryAttempts
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && selectionIds.Contains(item.RecipientSelectionId))
            .ToArrayAsync(cancellationToken);
        var responses = await db.RecipientResponses
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version
                && practitionerIds.Contains(item.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var assignments = await db.ResponsibilityAssignments
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version
                && practitionerIds.Contains(item.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var outbox = (await db.OutboxMessages
                .AsNoTracking()
                .Where(item => item.OrganizationId == organizationId
                    && item.AggregateId == alertId.Value
                    && item.EventType == "AlertDispatchRequested")
                .ToArrayAsync(cancellationToken))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id.Value)
            .FirstOrDefault();

        var recipientViews = selections
            .GroupBy(item => item.PractitionerId)
            .Where(group => practitioners.ContainsKey(group.Key))
            .Select(group =>
            {
                var practitioner = practitioners[group.Key];
                var recipientSelectionIds = group.Select(item => item.Id).ToHashSet();
                var recipientResponses = responses.Where(item => item.PractitionerId == group.Key).ToArray();
                var acknowledgement = recipientResponses.SingleOrDefault(item => item.IsAcknowledgement);
                var terminal = recipientResponses.SingleOrDefault(item => item.IsTerminalDisposition);
                var callUnit = recipientResponses.SingleOrDefault(item => item.IsCallUnitRequest);
                var lastResponse = recipientResponses.OrderByDescending(item => item.OccurredAtUtc).FirstOrDefault();
                var assignment = assignments.SingleOrDefault(item => item.PractitionerId == group.Key);
                var recipientAttempts = attempts
                    .Where(item => recipientSelectionIds.Contains(item.RecipientSelectionId))
                    .OrderBy(item => item.Channel)
                    .ThenBy(item => item.AttemptNumber)
                    .Select(item => new AlertLiveAttemptView(
                        item.Channel.ToString(),
                        item.AttemptNumber,
                        item.Status.ToString(),
                        item.OpenedState.ToString(),
                        item.OpenedAtUtc,
                        item.RequestedAtUtc,
                        item.SubmittedAtUtc,
                        item.DeliveredAtUtc,
                        item.FailedAtUtc,
                        SafeFailureCategory(item.FailureCategory)))
                    .ToArray();

                return new AlertLiveRecipientView(
                    practitioner.Id.Value,
                    practitioner.SimulationCode,
                    $"{practitioner.FirstName} {practitioner.LastName}",
                    practitioner.Specialty,
                    group.Select(item => item.OnCallSnapshot)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .FirstOrDefault(),
                    acknowledgement?.OccurredAtUtc,
                    terminal?.ResponseType.ToString(),
                    assignment?.AcceptedAtUtc,
                    callUnit?.OccurredAtUtc,
                    lastResponse?.SanitizedReasonCode,
                    recipientAttempts,
                    group.Select(item => item.SelectionSource.ToString()).Distinct().Order(StringComparer.Ordinal).ToArray());
            })
            .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.SimulationCode, StringComparer.Ordinal)
            .ToArray();

        var refreshedAtUtc = time.GetUtcNow();
        if (refreshedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidOperationException("The live projection clock must be UTC.");
        }

        var manualFallbackRequired = alert.State == AlertState.Failed
            || attempts.Any(attempt => attempt.Status == DeliveryAttemptStatus.Failed);
        var hasActiveResponsibility = assignments.Any(assignment => assignment.ReleasedAtUtc is null);
        var approval = await db.ConfirmedEscalationPlans.AsNoTracking().SingleOrDefaultAsync(row =>
            row.OrganizationId == organizationId && row.AlertId == alertId && row.AlertVersion == version, cancellationToken);
        var run = approval is null ? null : await db.EscalationRuns.AsNoTracking().SingleOrDefaultAsync(row => row.OrganizationId == organizationId
            && row.AlertId == alertId && row.AlertVersion == version, cancellationToken);
        AlertLiveEscalationView? escalation = null;
        if (approval is not null)
        {
            var events = run is null ? [] : await db.EscalationEvents.AsNoTracking()
                .Where(row => row.OrganizationId == organizationId && row.RunId == run.Id).OrderBy(row => row.Sequence).ToArrayAsync(cancellationToken);
            var controllable = alert.State == AlertState.Active && !hasActiveResponsibility;
            escalation = new(approval.PolicyId.Value, approval.PolicyVersion, run?.State.ToString() ?? "AwaitingActivation",
                run?.CurrentStep ?? 0, run?.State == EscalationRunState.Scheduled ? run.NextDueAtUtc : null,
                run?.RemainingDelay?.TotalSeconds, run?.StopReason,
                controllable && run?.State == EscalationRunState.Scheduled,
                controllable && run?.State == EscalationRunState.Paused,
                events.Select(row => new AlertLiveEscalationEvent(row.Sequence, row.Kind.ToString(), row.Step,
                    row.OccurredAtUtc, row.RecipientSelectionId, row.ActorUserId?.Value)).ToArray());
            manualFallbackRequired |= !hasActiveResponsibility && alert.State == AlertState.Active
                && run?.State is EscalationRunState.Exhausted or EscalationRunState.Failed;
        }

        var directoryStale = await db.DirectorySourceRecords.AnyAsync(item => item.OrganizationId == organizationId
            && item.IsStale && db.AlertRecipientSelections.Any(selection => selection.OrganizationId == organizationId
                && selection.AlertId == alertId && selection.AlertVersion == version && selection.PractitionerId == item.PractitionerId), cancellationToken);
        var latestSync = await db.DirectorySyncRuns.AsNoTracking().Where(item => item.OrganizationId == organizationId)
            .OrderByDescending(item => item.StartedAtUtc).ThenByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);
        // A fixed technical DEMO observation grace, not a clinical deadline or production threshold.
        var overdue = refreshedAtUtc.AddSeconds(-30);
        var warnings = OperationalWarnings.From(new(
            ProviderUnavailable: attempts.Any(item => item.Status == DeliveryAttemptStatus.Failed && item.FailureCategory == "provider-unavailable"),
            DispatchDelayed: outbox is not null && ((outbox.ProcessingState == OutboxProcessingState.Pending && outbox.NextAttemptAtUtc < overdue)
                || (outbox.ProcessingState == OutboxProcessingState.Processing && outbox.LeaseExpiresAtUtc < overdue)),
            DeliveryFailed: attempts.Any(item => item.Status == DeliveryAttemptStatus.Failed) || outbox?.ProcessingState == OutboxProcessingState.Failed,
            DirectoryStale: directoryStale,
            DirectorySynchronizationFailed: latestSync?.Status is DirectorySyncRunStatus.Failed or DirectorySyncRunStatus.Partial,
            EscalationProcessingDelayed: (run is { State: EscalationRunState.Scheduled or EscalationRunState.Running } && run.NextDueAtUtc < overdue)
                || run?.StopReason == EscalationEventKind.ProcessingFailed.ToString(),
            EscalationExhausted: run?.State == EscalationRunState.Exhausted && !hasActiveResponsibility));
        var result = new AlertLiveView(
            alert.Id.Value,
            version.Value,
            alert.State.ToString(),
            outbox?.ProcessingState.ToString() ?? "NotCreated",
            refreshedAtUtc,
            alert.State == AlertState.Active && hasActiveResponsibility,
            alert.State == AlertState.Active,
            manualFallbackRequired,
            recipientViews,
            escalation)
        { OperationalWarnings = warnings };
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static string? SafeFailureCategory(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= 64
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                ? value
                : "delivery-failed";
    }
}
