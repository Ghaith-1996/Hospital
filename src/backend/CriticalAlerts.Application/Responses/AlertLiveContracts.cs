using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Responses;

public sealed record AlertLiveView(
    Guid AlertId,
    int ConfirmedVersion,
    string AlertState,
    string OutboxState,
    DateTimeOffset RefreshedAtUtc,
    IReadOnlyList<AlertLiveRecipientView> Recipients);

public sealed record AlertLiveRecipientView(
    Guid PractitionerId,
    string SimulationCode,
    string DisplayName,
    string Specialty,
    string? OnCallSnapshot,
    DateTimeOffset? AcknowledgedAtUtc,
    string? TerminalDisposition,
    DateTimeOffset? ResponsibilityAcceptedAtUtc,
    IReadOnlyList<AlertLiveAttemptView> Attempts);

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
