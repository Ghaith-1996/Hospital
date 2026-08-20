namespace CriticalAlerts.Domain.Delivery;

public sealed class DeliveryEvent
{
    private DeliveryEvent()
    {
        EventType = string.Empty;
        ProviderEventId = string.Empty;
        SanitizedMetadata = string.Empty;
    }

    private DeliveryEvent(
        DeliveryEventId id,
        OrganizationId organizationId,
        DeliveryAttemptId deliveryAttemptId,
        string eventType,
        string providerEventId,
        DateTimeOffset receivedAtUtc,
        string sanitizedMetadata)
    {
        Id = id;
        OrganizationId = organizationId;
        DeliveryAttemptId = deliveryAttemptId;
        EventType = eventType;
        ProviderEventId = providerEventId;
        ReceivedAtUtc = receivedAtUtc;
        SanitizedMetadata = sanitizedMetadata;
    }

    public DeliveryEventId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public DeliveryAttemptId DeliveryAttemptId { get; private set; }

    public string EventType { get; private set; }

    public string ProviderEventId { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string SanitizedMetadata { get; private set; }

    public static DeliveryEvent Create(
        DeliveryEventId id,
        OrganizationId organizationId,
        DeliveryAttemptId deliveryAttemptId,
        string eventType,
        string providerEventId,
        DateTimeOffset receivedAtUtc,
        string sanitizedMetadata)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(providerEventId))
        {
            throw new DomainException("Delivery events require a type and provider event ID.");
        }

        return new DeliveryEvent(
            id,
            organizationId,
            deliveryAttemptId,
            eventType.Trim(),
            providerEventId.Trim(),
            UtcInstant.Require(receivedAtUtc, nameof(receivedAtUtc)),
            sanitizedMetadata.Trim());
    }
}
