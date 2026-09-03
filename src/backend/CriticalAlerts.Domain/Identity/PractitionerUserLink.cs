namespace CriticalAlerts.Domain.Identity;

public sealed class PractitionerUserLink
{
    private PractitionerUserLink()
    {
    }

    private PractitionerUserLink(
        PractitionerUserLinkId id,
        OrganizationId organizationId,
        UserId userId,
        PractitionerId practitionerId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        PractitionerId = practitionerId;
        CreatedAtUtc = createdAtUtc;
    }

    public PractitionerUserLinkId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public UserId UserId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static PractitionerUserLink Create(
        PractitionerUserLinkId id,
        OrganizationId organizationId,
        UserId userId,
        PractitionerId practitionerId,
        DateTimeOffset createdAtUtc)
    {
        return new PractitionerUserLink(
            id,
            organizationId,
            userId,
            practitionerId,
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
