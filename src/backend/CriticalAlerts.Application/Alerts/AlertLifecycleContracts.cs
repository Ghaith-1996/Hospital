using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Alerts;

public sealed record AlertLifecycleActionRequest(int ExpectedVersion);

public sealed record AlertLifecycleResult(
    Guid AlertId,
    int ConfirmedVersion,
    string State,
    bool Replayed);

public interface IAlertLifecycleService
{
    Task<AlertLifecycleResult?> ResolveAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        AlertLifecycleActionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<AlertLifecycleResult?> CancelAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        AlertLifecycleActionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class AlertLifecycleValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}
