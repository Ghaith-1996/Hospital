namespace CriticalAlerts.Domain.Directory;

public sealed class PractitionerRoleAssignment
{
    private PractitionerRoleAssignment()
    {
        Title = string.Empty;
    }

    private PractitionerRoleAssignment(
        PractitionerRoleId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        DepartmentId departmentId,
        string title,
        bool isPrimary)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        DepartmentId = departmentId;
        Title = title;
        IsPrimary = isPrimary;
    }

    public PractitionerRoleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public DepartmentId DepartmentId { get; private set; }

    public string Title { get; private set; }

    public bool IsPrimary { get; private set; }

    public static PractitionerRoleAssignment Create(
        PractitionerRoleId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        DepartmentId departmentId,
        string title,
        bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Practitioner roles require a title.");
        }

        return new PractitionerRoleAssignment(id, organizationId, practitionerId, departmentId, title.Trim(), isPrimary);
    }
}
