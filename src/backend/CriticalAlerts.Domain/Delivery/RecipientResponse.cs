namespace CriticalAlerts.Domain.Delivery;

public sealed class RecipientResponse
{
    private static readonly IReadOnlyDictionary<RecipientResponseType, IReadOnlySet<string>> AllowedReasonCodes =
        new Dictionary<RecipientResponseType, IReadOnlySet<string>>
        {
            [RecipientResponseType.Acknowledged] = new HashSet<string>(StringComparer.Ordinal)
            {
                "simulation-acknowledged",
            },
            [RecipientResponseType.Accepted] = new HashSet<string>(StringComparer.Ordinal)
            {
                "simulation-responsibility-accepted",
            },
            [RecipientResponseType.Declined] = new HashSet<string>(StringComparer.Ordinal)
            {
                "simulation-declined",
                "simulation-not-my-service",
                "simulation-wrong-specialty",
                "simulation-not-available",
            },
            [RecipientResponseType.Unavailable] = new HashSet<string>(StringComparer.Ordinal)
            {
                "simulation-unavailable",
                "simulation-no-coverage",
                "simulation-not-on-call",
            },
            [RecipientResponseType.CallUnitRequested] = new HashSet<string>(StringComparer.Ordinal)
            {
                "simulation-call-unit-requested",
            },
        };

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
        if (string.IsNullOrWhiteSpace(sanitizedReasonCode))
        {
            throw new DomainException("Recipient responses require an allowlisted simulation reason code.");
        }

        var reasonCode = sanitizedReasonCode.Trim();
        if (reasonCode.Length > 64
            || !AllowedReasonCodes.TryGetValue(responseType, out var allowedReasonCodes)
            || !allowedReasonCodes.Contains(reasonCode))
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
            CategoryFor(responseType),
            actorUserId,
            UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc)),
            reasonCode);
    }

    public bool ImpliesResponsibilityAcceptance => ResponseType == RecipientResponseType.Accepted;

    public bool IsAcknowledgement => ResponseType == RecipientResponseType.Acknowledged;

    public bool IsCallUnitRequest => ResponseType == RecipientResponseType.CallUnitRequested;

    public bool IsTerminalDisposition => ResponseType is
        RecipientResponseType.Accepted or RecipientResponseType.Declined or RecipientResponseType.Unavailable;

    public static RecipientResponseCategory CategoryFor(RecipientResponseType responseType)
        => responseType switch
        {
            RecipientResponseType.Acknowledged => RecipientResponseCategory.Acknowledgement,
            RecipientResponseType.CallUnitRequested => RecipientResponseCategory.CallUnitRequest,
            RecipientResponseType.Accepted or RecipientResponseType.Declined or RecipientResponseType.Unavailable
                => RecipientResponseCategory.TerminalDisposition,
            _ => throw new DomainException("Recipient responses require an available simulation response type."),
        };

    public static string DefaultReasonCode(RecipientResponseType responseType)
        => responseType switch
        {
            RecipientResponseType.Acknowledged => "simulation-acknowledged",
            RecipientResponseType.Accepted => "simulation-responsibility-accepted",
            RecipientResponseType.Declined => "simulation-declined",
            RecipientResponseType.Unavailable => "simulation-unavailable",
            RecipientResponseType.CallUnitRequested => "simulation-call-unit-requested",
            _ => throw new DomainException("Recipient responses require an available simulation response type."),
        };
}
