using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Escalation;

public sealed record EscalationOverrideRequest(int ExpectedVersion, string? ReasonCode);

public sealed record EscalationOverrideResult(Guid AlertId, int ConfirmedVersion, Guid EscalationRunId,
    string State, string ReasonCode, DateTimeOffset OccurredAtUtc, bool Replayed);

public interface IEscalationOverrideService
{
    Task<EscalationOverrideResult?> PauseAsync(OrganizationId organizationId, UserId actorUserId, string correlationId,
        AlertId alertId, EscalationOverrideRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<EscalationOverrideResult?> ResumeAsync(OrganizationId organizationId, UserId actorUserId, string correlationId,
        AlertId alertId, EscalationOverrideRequest request, string? idempotencyKey, CancellationToken cancellationToken);
}

public sealed class EscalationOverrideValidationException(string code, string message, bool conflict = false) : DomainException(message)
{
    public string Code { get; } = code;
    public bool IsConflict { get; } = conflict;
}
