namespace CriticalAlerts.Domain.Delivery;

public sealed class ResponsibilityAssignment
{
    private ResponsibilityAssignment()
    {
        ReasonCode = string.Empty;
    }

    private ResponsibilityAssignment(
        ResponsibilityAssignmentId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        PractitionerId practitionerId,
        UserId actorUserId,
        RecipientResponseId sourceResponseId,
        DateTimeOffset acceptedAtUtc,
        string reasonCode)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        PractitionerId = practitionerId;
        ActorUserId = actorUserId;
        SourceResponseId = sourceResponseId;
        AcceptedAtUtc = acceptedAtUtc;
        ReasonCode = reasonCode;
    }

    public ResponsibilityAssignmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertDraftVersion AlertVersion { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public UserId ActorUserId { get; private set; }

    public RecipientResponseId SourceResponseId { get; private set; }

    public DateTimeOffset AcceptedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public string ReasonCode { get; private set; }

    public static ResponsibilityAssignment Accept(
        ResponsibilityAssignmentId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        PractitionerId practitionerId,
        UserId actorUserId,
        RecipientResponseId sourceResponseId,
        DateTimeOffset acceptedAtUtc,
        string reasonCode)
    {
        return new ResponsibilityAssignment(
            id,
            organizationId,
            alertId,
            alertVersion,
            practitionerId,
            actorUserId,
            sourceResponseId,
            UtcInstant.Require(acceptedAtUtc, nameof(acceptedAtUtc)),
            reasonCode.Trim());
    }

    public static ResponsibilityAssignment? FromResponse(RecipientResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.ImpliesResponsibilityAcceptance)
        {
            return null;
        }

        return Accept(
            ResponsibilityAssignmentId.New(),
            response.OrganizationId,
            response.AlertId,
            response.AlertVersion,
            response.PractitionerId,
            response.ActorUserId,
            response.Id,
            response.OccurredAtUtc,
            "responsibility-accepted");
    }
}
