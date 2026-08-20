namespace CriticalAlerts.Domain.Delivery;

public sealed class RecipientResponse
{
    private RecipientResponse()
    {
        SanitizedReasonCode = string.Empty;
    }

    private RecipientResponse(
        RecipientResponseId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertRecipientSelectionId recipientSelectionId,
        RecipientResponseType responseType,
        UserId actorUserId,
        DateTimeOffset occurredAtUtc,
        string sanitizedReasonCode)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        RecipientSelectionId = recipientSelectionId;
        ResponseType = responseType;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        SanitizedReasonCode = sanitizedReasonCode;
    }

    public RecipientResponseId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertRecipientSelectionId RecipientSelectionId { get; private set; }

    public RecipientResponseType ResponseType { get; private set; }

    public UserId ActorUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string SanitizedReasonCode { get; private set; }

    public static RecipientResponse Record(
        RecipientResponseId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertRecipientSelectionId recipientSelectionId,
        RecipientResponseType responseType,
        UserId actorUserId,
        DateTimeOffset occurredAtUtc,
        string sanitizedReasonCode)
    {
        return new RecipientResponse(
            id,
            organizationId,
            alertId,
            recipientSelectionId,
            responseType,
            actorUserId,
            UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc)),
            sanitizedReasonCode.Trim());
    }

    public bool ImpliesResponsibilityAcceptance => ResponseType == RecipientResponseType.Accepted;
}
