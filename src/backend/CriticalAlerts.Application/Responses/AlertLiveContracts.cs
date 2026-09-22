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
    AlertLiveEscalationView? Escalation = null);

public sealed record AlertLiveEscalationView(Guid PolicyId, string PolicyVersion, string State, int CurrentStep,
    DateTimeOffset? NextDueAtUtc, double? RemainingDelaySeconds, string? StopReason, bool CanPause, bool CanResume,
    IReadOnlyList<AlertLiveEscalationEvent> Events);
public sealed record AlertLiveEscalationEvent(int Sequence, string Kind, int Step, DateTimeOffset OccurredAtUtc,
    Guid? RecipientSelectionId, Guid? ActorUserId);

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
    IReadOnlyList<string>? SelectionSources = null);

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

public interface IAlertLiveQueryService
{
    Task<AlertLiveView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken);
}
