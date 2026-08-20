namespace CriticalAlerts.Domain.Identity;

public sealed class UserRole
{
    private UserRole()
    {
    }

    private UserRole(OrganizationId organizationId, UserId userId, RoleId roleId)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
    }

    public OrganizationId OrganizationId { get; private set; }

    public UserId UserId { get; private set; }

    public RoleId RoleId { get; private set; }

    public static UserRole Create(OrganizationId organizationId, UserId userId, RoleId roleId)
        => new(organizationId, userId, roleId);
}
