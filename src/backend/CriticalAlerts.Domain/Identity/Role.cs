namespace CriticalAlerts.Domain.Identity;

public sealed class Role
{
    private Role()
    {
        Name = string.Empty;
    }

    private Role(RoleId id, OrganizationId organizationId, string name)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
    }

    public RoleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; }

    public static Role Create(RoleId id, OrganizationId organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Role name is required.");
        }

        return new Role(id, organizationId, name.Trim());
    }
}
