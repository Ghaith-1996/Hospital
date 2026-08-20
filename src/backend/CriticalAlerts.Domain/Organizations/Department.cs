namespace CriticalAlerts.Domain.Organizations;

public sealed class Department
{
    private Department()
    {
        Name = string.Empty;
    }

    private Department(
        DepartmentId id,
        OrganizationId organizationId,
        SiteId siteId,
        string name,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        SiteId = siteId;
        Name = name;
        CreatedAtUtc = createdAtUtc;
    }

    public DepartmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public SiteId SiteId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Department Create(
        DepartmentId id,
        OrganizationId organizationId,
        SiteId siteId,
        string name,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Department name is required.");
        }

        return new Department(id, organizationId, siteId, name.Trim(), UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
