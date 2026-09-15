using System.Data;
using System.Text.Json;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Responses;

public sealed class AlertLiveQueryService(CriticalAlertsDbContext db) : IAlertLiveQueryService
{
    public async Task<AlertLiveView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        bool canOperateLifecycle,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var alert = await db.Alerts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Id == alertId
                && item.ConfirmedDraftVersion != null,
                cancellationToken);
        if (alert is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var version = alert.ConfirmedDraftVersion!.Value;
        var plan = await db.AlertEscalationPlans
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version.Value,
                cancellationToken);
        var automaticEscalationEligible = plan is not null
            && alert.AutomaticEscalationEligible
            && alert.ExactEscalationPlanId == plan.Id
            && alert.ExactEscalationPolicyId == plan.EscalationPolicyId
            && alert.ExactEscalationPolicyVersion == plan.EscalationPolicyVersion
            && alert.ExactEscalationPlanRevision == plan.Revision;
        var run = automaticEscalationEligible
            ? await db.EscalationRuns.AsNoTracking().SingleOrDefaultAsync(item =>
                item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version
                && item.PlanId == plan!.Id
                && item.PolicyId == plan.EscalationPolicyId
                && item.PolicyVersion == plan.EscalationPolicyVersion
                && item.PlanRevision == plan.Revision,
                cancellationToken)
            : null;

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
                && item.AlertVersion == version)
            .ToArrayAsync(cancellationToken);
        var originalOutbox = (await db.OutboxMessages
                .AsNoTracking()
                .Where(item => item.OrganizationId == organizationId
                    && item.AggregateId == alertId.Value
                    && item.EventType == "AlertDispatchRequested")
                .ToArrayAsync(cancellationToken))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id.Value)
            .FirstOrDefault();
        var escalationOutboxes = run is null
            ? []
            : (await db.OutboxMessages
                .AsNoTracking()
                .Where(item => item.OrganizationId == organizationId
                    && item.AggregateId == alertId.Value
                    && item.EventType == nameof(EscalationDispatchRequested))
                .ToArrayAsync(cancellationToken))
                .Where(item => MatchesEscalationOutbox(item.PayloadJson, run.Id, version.Value))
                .ToArray();
        var events = run is null
            ? []
            : await db.EscalationEvents
                .AsNoTracking()
                .Where(item => item.OrganizationId == organizationId
                    && item.AlertId == alertId
                    && item.AlertVersion == version
                    && item.RunId == run.Id)
                .ToArrayAsync(cancellationToken);

        var activationBySelection = events
            .Where(item => item.EventType == EscalationEventType.RecipientActivated
                && item.RecipientSelectionId is not null)
            .ToDictionary(item => item.RecipientSelectionId!.Value, item => item);
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
                var lastResponse = recipientResponses.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id.Value).FirstOrDefault();
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
                var selectionViews = group
                    .OrderBy(item => item.Channel)
                    .ThenBy(item => item.Id.Value)
                    .Select(item =>
                    {
                        activationBySelection.TryGetValue(item.Id, out var activation);
                        var hasExactProvenance = item.SelectionSource == RecipientSelectionSource.EscalationPolicy
                            && activation is not null
                            && run is not null;
                        return new AlertLiveRecipientSelectionView(
                            item.Id.Value,
                            item.PractitionerRoleId?.Value,
                            item.Channel.ToString(),
                            item.SelectionSource.ToString(),
                            item.SelectedAtUtc,
                            hasExactProvenance ? run!.Id.Value : null,
                            hasExactProvenance ? activation!.StepSequence : null,
                            hasExactProvenance ? run!.PolicyId.Value : null,
                            hasExactProvenance ? run!.PolicyVersion : null,
                            hasExactProvenance ? run!.PlanRevision : null);
                    })
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
                    selectionViews);
            })
            .OrderBy(item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(item => item.SimulationCode, StringComparer.Ordinal)
            .ToArray();

        var refreshedAtUtc = await new DatabaseClock(db).GetUtcNowAsync(cancellationToken);
        var hasActiveResponsibility = assignments.Any(assignment => assignment.ReleasedAtUtc is null);
        var lifecycleOrResponsibilityComplete = alert.State is AlertState.Resolved or AlertState.Cancelled
            || hasActiveResponsibility;
        var escalationOutboxState = GetEscalationOutboxState(escalationOutboxes);
        var failedEscalationOutbox = escalationOutboxes
            .Where(item => item.ProcessingState == OutboxProcessingState.Failed)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id.Value)
            .FirstOrDefault();
        var escalationFailure = run?.Outcome is EscalationOutcome.Exhausted or EscalationOutcome.ProcessingFailed;
        var manualFallbackRequired = !lifecycleOrResponsibilityComplete
            && (alert.State == AlertState.Failed
                || attempts.Any(attempt => attempt.Status == DeliveryAttemptStatus.Failed)
                || escalationFailure
                || failedEscalationOutbox is not null);
        var totalSteps = automaticEscalationEligible ? plan!.Definition.Steps.Count : (int?)null;
        var terminalRun = run?.State is EscalationRunState.Completed or EscalationRunState.Stopped;
        var currentStep = run is null || totalSteps is null
            ? (int?)null
            : Math.Min(run.CurrentStep, totalSteps.Value);
        var nextStep = run is not null && !terminalRun ? currentStep : null;
        var timeline = run is null
            ? []
            : events
                .OrderBy(item => item.OccurredAtUtc)
                .ThenBy(item => item.StepSequence)
                .ThenBy(item => EventOrder(item.EventType))
                .ThenBy(item => item.Id.Value)
                .Select(item => new AlertLiveEscalationEventView(
                    item.Id.Value,
                    run.Id.Value,
                    version.Value,
                    run.PolicyId.Value,
                    run.PolicyVersion,
                    run.PlanRevision!,
                    item.StepSequence,
                    item.EventType.ToString(),
                    item.OccurredAtUtc,
                    item.RecipientSelectionId?.Value,
                    item.FailureCategory?.ToString(),
                    item.OverrideReason?.ToString()))
                .ToArray();
        var canControlRun = canOperateLifecycle
            && automaticEscalationEligible
            && alert.State == AlertState.Active
            && run is not null
            && !hasActiveResponsibility
            && !terminalRun;
        var escalation = new AlertLiveEscalationView(
            SimulationOnly: true,
            TimingAuthority: "PostgreSQLUtc",
            automaticEscalationEligible,
            automaticEscalationEligible ? plan!.EscalationPolicyId.Value : null,
            automaticEscalationEligible ? plan!.EscalationPolicyVersion : null,
            automaticEscalationEligible ? plan!.Revision : null,
            run?.Id.Value,
            run?.State.ToString(),
            currentStep,
            nextStep,
            totalSteps,
            run is { State: EscalationRunState.Scheduled or EscalationRunState.Running } ? run.NextDueAtUtc : null,
            run?.State == EscalationRunState.Paused && run.RemainingDelay is { } remaining
                ? (long)Math.Ceiling(remaining.TotalSeconds)
                : null,
            run?.State == EscalationRunState.Paused,
            run?.State == EscalationRunState.Stopped,
            run?.Outcome == EscalationOutcome.Exhausted,
            run?.Outcome?.ToString(),
            SafeRunReason(run?.Outcome),
            run?.FailureCategory?.ToString(),
            escalationOutboxState,
            SafeFailureCategory(failedEscalationOutbox?.LastErrorCategory ?? string.Empty),
            manualFallbackRequired,
            canControlRun && run!.State is EscalationRunState.Scheduled or EscalationRunState.Running,
            canControlRun && run!.State == EscalationRunState.Paused,
            timeline);

        var result = new AlertLiveView(
            alert.Id.Value,
            version.Value,
            alert.State.ToString(),
            originalOutbox?.ProcessingState.ToString() ?? "NotCreated",
            refreshedAtUtc,
            alert.State == AlertState.Active && hasActiveResponsibility,
            alert.State == AlertState.Active,
            manualFallbackRequired,
            recipientViews,
            escalation);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static string? SafeFailureCategory(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return value.Length <= 64
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                ? value
                : "delivery-failed";
    }

    private static string? SafeRunReason(EscalationOutcome? outcome) => outcome switch
    {
        EscalationOutcome.ResponsibilityAccepted => "responsibility-accepted",
        EscalationOutcome.Resolved => "alert-resolved",
        EscalationOutcome.Cancelled => "alert-cancelled",
        EscalationOutcome.Exhausted => "automatic-steps-exhausted",
        EscalationOutcome.ProcessingFailed => "manual-fallback-required",
        _ => null,
    };

    private static int EventOrder(EscalationEventType type) => type switch
    {
        EscalationEventType.Scheduled => 0,
        EscalationEventType.StepDue => 1,
        EscalationEventType.RecipientActivated => 2,
        EscalationEventType.DispatchQueued => 3,
        EscalationEventType.Paused => 4,
        EscalationEventType.Resumed => 5,
        EscalationEventType.StoppedByResponsibility => 6,
        EscalationEventType.StoppedByResolution => 7,
        EscalationEventType.StoppedByCancellation => 8,
        EscalationEventType.Exhausted => 9,
        EscalationEventType.ProcessingFailed => 10,
        _ => int.MaxValue,
    };

    private static string GetEscalationOutboxState(IReadOnlyList<OutboxMessage> messages)
    {
        if (messages.Count == 0) return "NotCreated";
        if (messages.Any(item => item.ProcessingState == OutboxProcessingState.Failed)) return "Failed";
        if (messages.Any(item => item.ProcessingState == OutboxProcessingState.Processing)) return "Processing";
        if (messages.Any(item => item.ProcessingState == OutboxProcessingState.Pending)) return "Pending";
        return "Processed";
    }

    private static bool MatchesEscalationOutbox(string payloadJson, EscalationRunId runId, int alertVersion)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            return root.TryGetProperty("alertVersion", out var version)
                && version.TryGetInt32(out var parsedVersion)
                && parsedVersion == alertVersion
                && root.TryGetProperty("escalationRunId", out var run)
                && run.ValueKind == JsonValueKind.String
                && Guid.TryParse(run.GetString(), out var parsedRun)
                && parsedRun == runId.Value;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
