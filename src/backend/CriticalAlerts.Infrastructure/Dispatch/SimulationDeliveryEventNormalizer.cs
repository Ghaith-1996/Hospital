using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class SimulationDeliveryEventNormalizer : INotificationStatusNormalizer
{
    public NormalizedDeliveryEvent Normalize(
        DeliveryAttemptId deliveryAttemptId,
        NotificationProviderEvent providerEvent)
    {
        if (string.IsNullOrWhiteSpace(providerEvent.ProviderEventId)
            || providerEvent.ProviderEventId.Length > 100
            || string.IsNullOrWhiteSpace(providerEvent.SanitizedMetadata)
            || providerEvent.SanitizedMetadata.Length > 500
            || providerEvent.SanitizedMetadata.Any(char.IsControl)
            || providerEvent.OccurredAtUtc.Offset != TimeSpan.Zero
            || (providerEvent.FailureCategory is not null && !IsSafeCategory(providerEvent.FailureCategory)))
        {
            throw new DispatchValidationException("provider-event-invalid", "The simulation provider event is invalid.");
        }

        var status = providerEvent.EventType switch
        {
            "submitted" => DeliveryAttemptStatus.Submitted,
            "delivered" => DeliveryAttemptStatus.Delivered,
            "failed" => DeliveryAttemptStatus.Failed,
            _ => throw new DispatchValidationException("provider-event-type-invalid", "The simulation provider event type is not supported."),
        };

        return new NormalizedDeliveryEvent(
            deliveryAttemptId,
            providerEvent.ProviderEventId,
            status,
            providerEvent.OccurredAtUtc,
            providerEvent.FailureCategory,
            providerEvent.SanitizedMetadata);
    }

    private static bool IsSafeCategory(string value)
        => value.Length is > 0 and <= 64
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
