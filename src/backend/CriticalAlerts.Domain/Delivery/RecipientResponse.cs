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
        AlertDraftVersion alertVersion,
        PractitionerId practitionerId,
        RecipientResponseType responseType,
        RecipientResponseCategory category,
        UserId actorUserId,
        DateTimeOffset occurredAtUtc,
        string sanitizedReasonCode)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        PractitionerId = practitionerId;
        ResponseType = responseType;
        Category = category;
        ActorUserId = actorUserId;
        OccurredAtUtc = occurredAtUtc;
        SanitizedReasonCode = sanitizedReasonCode;
    }

    public RecipientResponseId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertDraftVersion AlertVersion { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public RecipientResponseType ResponseType { get; private set; }

    public RecipientResponseCategory Category { get; private set; }

    public UserId ActorUserId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string SanitizedReasonCode { get; private set; }

    public static RecipientResponse Record(
        RecipientResponseId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        PractitionerId practitionerId,
        RecipientResponseType responseType,
        UserId actorUserId,
        DateTimeOffset occurredAtUtc,
        string sanitizedReasonCode)
    {
        if (responseType == RecipientResponseType.CallUnitRequested)
        {
            throw new DomainException("Call-unit requests are not available in the Phase 8 simulation.");
        }

        if (string.IsNullOrWhiteSpace(sanitizedReasonCode))
        {
            throw new DomainException("Recipient responses require an allowlisted simulation reason code.");
        }

        var reasonCode = sanitizedReasonCode.Trim();
        var expectedReasonCode = responseType switch
        {
            RecipientResponseType.Acknowledged => "simulation-acknowledged",
            RecipientResponseType.Accepted => "simulation-responsibility-accepted",
            RecipientResponseType.Declined => "simulation-declined",
            RecipientResponseType.Unavailable => "simulation-unavailable",
            _ => throw new DomainException("Recipient responses require an available Phase 8 response type."),
        };
        if (reasonCode.Length > 64
            || !string.Equals(reasonCode, expectedReasonCode, StringComparison.Ordinal))
        {
            throw new DomainException("Recipient responses require an allowlisted simulation reason code.");
        }

        return new RecipientResponse(
            id,
            organizationId,
            alertId,
            alertVersion,
            practitionerId,
            responseType,
            responseType == RecipientResponseType.Acknowledged
                ? RecipientResponseCategory.Acknowledgement
                : RecipientResponseCategory.TerminalDisposition,
            actorUserId,
            UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc)),
            reasonCode);
    }

    public bool ImpliesResponsibilityAcceptance => ResponseType == RecipientResponseType.Accepted;

    public bool IsAcknowledgement => ResponseType == RecipientResponseType.Acknowledged;

    public bool IsTerminalDisposition => ResponseType is
        RecipientResponseType.Accepted or RecipientResponseType.Declined or RecipientResponseType.Unavailable;
}
