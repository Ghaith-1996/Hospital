using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Application.Dispatch;

public sealed record NotificationDispatchRequest(
    OrganizationId OrganizationId,
    AlertId AlertId,
    AlertDraftVersion DraftVersion,
    AlertRecipientSelectionId RecipientSelectionId,
    NotificationChannel Channel,
    string EndpointReference,
    string MessageReference,
    string WakeUpText,
    string IdempotencyKey,
    string CorrelationId,
    DeliveryAttemptStatus CurrentAttemptStatus = DeliveryAttemptStatus.Requested);

public sealed record NotificationProviderEvent(
    string ProviderEventId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string SanitizedMetadata,
    string? FailureCategory = null);

public sealed record NotificationDispatchResult(
    string ProviderReference,
    IReadOnlyList<NotificationProviderEvent> Events,
    bool Retryable,
    string? FailureCategory,
    DateTimeOffset? RetryAtUtc = null);

public sealed record NormalizedDeliveryEvent(
    DeliveryAttemptId DeliveryAttemptId,
    string ProviderEventId,
    DeliveryAttemptStatus Status,
    DateTimeOffset OccurredAtUtc,
    string? FailureCategory,
    string SanitizedMetadata);

public interface INotificationChannel
{
    NotificationChannel ChannelType { get; }

    string ProviderName { get; }

    Task<NotificationDispatchResult> DispatchAsync(
        NotificationDispatchRequest request,
        SimulationDispatchScenario scenario,
        CancellationToken cancellationToken);
}

public interface INotificationStatusNormalizer
{
    NormalizedDeliveryEvent Normalize(
        DeliveryAttemptId deliveryAttemptId,
        NotificationProviderEvent providerEvent);
}

public sealed record DeliveryStatusView(
    Guid AlertId,
    int ConfirmedVersion,
    string AlertState,
    string OutboxState,
    IReadOnlyList<DeliveryAttemptView> Attempts);

public sealed record DeliveryAttemptView(
    Guid RecipientSelectionId,
    string Channel,
    int AttemptNumber,
    string Provider,
    string Status,
    string OpenedState,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? FailureCategory);

public interface IDeliveryStatusQueryService
{
    Task<DeliveryStatusView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken);
}

public sealed record DispatchProcessingResult(
    bool Processed,
    bool Rescheduled,
    bool PermanentlyFailed,
    Guid? OutboxMessageId,
    string Outcome);

public interface IOutboxDispatchProcessor
{
    Task<DispatchProcessingResult> ProcessNextAsync(
        string leaseOwner,
        CancellationToken cancellationToken);
}
