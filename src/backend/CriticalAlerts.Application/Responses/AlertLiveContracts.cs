using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Responses;

public sealed record AlertLiveView(
    Guid AlertId,
    int ConfirmedVersion,
    string AlertState,
    string OutboxState,
    DateTimeOffset RefreshedAtUtc,
    bool CanResolve,
    bool CanCancel,
    bool ManualFallbackRequired,
    IReadOnlyList<AlertLiveRecipientView> Recipients,
    AlertLiveEscalationView Escalation);

public sealed record AlertLiveRecipientView(
    Guid PractitionerId,
    string SimulationCode,
    string DisplayName,
    string Specialty,
    string? OnCallSnapshot,
    DateTimeOffset? AcknowledgedAtUtc,
    string? TerminalDisposition,
    DateTimeOffset? ResponsibilityAcceptedAtUtc,
    DateTimeOffset? CallUnitRequestedAtUtc,
    string? LastResponseReasonCode,
    IReadOnlyList<AlertLiveAttemptView> Attempts,
    IReadOnlyList<AlertLiveRecipientSelectionView> Selections);

public sealed record AlertLiveRecipientSelectionView(
    Guid SelectionId,
    Guid? PractitionerRoleId,
    string Channel,
    string SelectionSource,
    DateTimeOffset SelectedAtUtc,
    Guid? EscalationRunId,
    int? EscalationStepSequence,
    Guid? EscalationPolicyId,
    string? EscalationPolicyVersion,
    string? EscalationPlanRevision);

public sealed record AlertLiveAttemptView(
    string Channel,
    int AttemptNumber,
    string Status,
    string OpenedState,
    DateTimeOffset? OpenedAtUtc,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? FailureCategory);

public sealed record AlertLiveEscalationView(
    bool SimulationOnly,
    string TimingAuthority,
    bool AutomaticEscalationEligible,
    Guid? PolicyId,
    string? PolicyVersion,
    string? PlanRevision,
    Guid? RunId,
    string? RunState,
    int? CurrentStep,
    int? NextStepSequence,
    int? TotalSteps,
    DateTimeOffset? NextEvaluationAtUtc,
    long? RemainingPauseSeconds,
    bool Paused,
    bool Stopped,
    bool Exhausted,
    string? TerminalOutcome,
    string? ReasonCode,
    string? FailureCategory,
    string EscalationOutboxState,
    string? EscalationOutboxFailureCategory,
    bool ManualFallbackRequired,
    bool CanPause,
    bool CanResume,
    IReadOnlyList<AlertLiveEscalationEventView> Timeline);

public sealed record AlertLiveEscalationEventView(
    Guid EventId,
    Guid RunId,
    int AlertVersion,
    Guid PolicyId,
    string PolicyVersion,
    string PlanRevision,
    int StepSequence,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    Guid? RecipientSelectionId,
    string? FailureCategory,
    string? OverrideReason);

public interface IAlertLiveQueryService
{
    Task<AlertLiveView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        bool canOperateLifecycle,
        CancellationToken cancellationToken);
}
