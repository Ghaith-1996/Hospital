namespace CriticalAlerts.Domain.Delivery;

public sealed class DeliveryAttempt
{
    private DeliveryAttempt()
    {
        IdempotencyKey = string.Empty;
        Provider = string.Empty;
        ProviderReference = string.Empty;
        FailureCategory = string.Empty;
    }

    private DeliveryAttempt(
        DeliveryAttemptId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertRecipientSelectionId recipientSelectionId,
        NotificationChannel channel,
        int attemptNumber,
        string idempotencyKey,
        string provider,
        DeliveryAttemptStatus status,
        ObservationState openedState,
        DateTimeOffset requestedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        RecipientSelectionId = recipientSelectionId;
        Channel = channel;
        AttemptNumber = attemptNumber;
        IdempotencyKey = idempotencyKey;
        Provider = provider;
        Status = status;
        OpenedState = openedState;
        RequestedAtUtc = requestedAtUtc;
        ProviderReference = string.Empty;
        FailureCategory = string.Empty;
    }

    public DeliveryAttemptId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertRecipientSelectionId RecipientSelectionId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public int AttemptNumber { get; private set; }

    public string IdempotencyKey { get; private set; }

    public string Provider { get; private set; }

    public string ProviderReference { get; private set; }

    public DeliveryAttemptStatus Status { get; private set; }

    public ObservationState OpenedState { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public DateTimeOffset? FailedAtUtc { get; private set; }

    public string FailureCategory { get; private set; }

    public static DeliveryAttempt CreateRequested(
        DeliveryAttemptId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertRecipientSelectionId recipientSelectionId,
        NotificationChannel channel,
        int attemptNumber,
        string idempotencyKey,
        string provider,
        DateTimeOffset requestedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(provider))
        {
            throw new DomainException("Delivery attempts require an idempotency key and provider name.");
        }

        var openedState = channel == NotificationChannel.SecureMessage
            ? ObservationState.PendingNotObserved
            : ObservationState.NotApplicable;

        return new DeliveryAttempt(
            id,
            organizationId,
            alertId,
            recipientSelectionId,
            channel,
            attemptNumber,
            idempotencyKey.Trim(),
            provider.Trim(),
            DeliveryAttemptStatus.Requested,
            openedState,
            UtcInstant.Require(requestedAtUtc, nameof(requestedAtUtc)));
    }

    public void MarkDelivered(DateTimeOffset deliveredAtUtc)
    {
        Status = DeliveryAttemptStatus.Delivered;
        DeliveredAtUtc = UtcInstant.Require(deliveredAtUtc, nameof(deliveredAtUtc));
    }
}
