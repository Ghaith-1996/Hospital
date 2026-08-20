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
        PractitionerId practitionerId,
        UserId actorUserId,
        DateTimeOffset acceptedAtUtc,
        string reasonCode)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        PractitionerId = practitionerId;
        ActorUserId = actorUserId;
        AcceptedAtUtc = acceptedAtUtc;
        ReasonCode = reasonCode;
    }

    public ResponsibilityAssignmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public UserId ActorUserId { get; private set; }

    public DateTimeOffset AcceptedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public string ReasonCode { get; private set; }

    public static ResponsibilityAssignment Accept(
        ResponsibilityAssignmentId id,
        OrganizationId organizationId,
        AlertId alertId,
        PractitionerId practitionerId,
        UserId actorUserId,
        DateTimeOffset acceptedAtUtc,
        string reasonCode)
    {
        return new ResponsibilityAssignment(
            id,
            organizationId,
            alertId,
            practitionerId,
            actorUserId,
            UtcInstant.Require(acceptedAtUtc, nameof(acceptedAtUtc)),
            reasonCode.Trim());
    }

    public static ResponsibilityAssignment? FromResponse(RecipientResponse response, PractitionerId practitionerId)
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
            practitionerId,
            response.ActorUserId,
            response.OccurredAtUtc,
            "responsibility-accepted");
    }
}
