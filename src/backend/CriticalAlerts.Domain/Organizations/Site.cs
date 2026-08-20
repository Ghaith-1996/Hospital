namespace CriticalAlerts.Domain.Organizations;

public sealed class Site
{
    private Site()
    {
        Name = string.Empty;
    }

    private Site(SiteId id, OrganizationId organizationId, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public SiteId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Site Create(SiteId id, OrganizationId organizationId, string name, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Site name is required.");
        }

        return new Site(id, organizationId, name.Trim(), UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
