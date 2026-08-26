namespace CriticalAlerts.Domain.Directory;

public sealed class PractitionerRoleAssignment
{
    private PractitionerRoleAssignment()
    {
        Title = string.Empty;
        SourceSystem = string.Empty;
        SourceRecordId = string.Empty;
    }

    private PractitionerRoleAssignment(
        PractitionerRoleId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        DepartmentId departmentId,
        string title,
        bool isPrimary,
        string sourceSystem,
        string sourceRecordId)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        DepartmentId = departmentId;
        Title = title;
        IsPrimary = isPrimary;
        SourceSystem = sourceSystem;
        SourceRecordId = sourceRecordId;
    }

    public PractitionerRoleId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public DepartmentId DepartmentId { get; private set; }

    public string Title { get; private set; }

    public bool IsPrimary { get; private set; }

    public string SourceSystem { get; private set; }

    public string SourceRecordId { get; private set; }

    public static PractitionerRoleAssignment Create(
        PractitionerRoleId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        DepartmentId departmentId,
        string title,
        bool isPrimary,
        string sourceSystem,
        string sourceRecordId)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Practitioner roles require a title.");
        }

        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(sourceRecordId))
        {
            throw new DomainException("Practitioner roles require a source system and source record.");
        }

        return new PractitionerRoleAssignment(
            id,
            organizationId,
            practitionerId,
            departmentId,
            title.Trim(),
            isPrimary,
            sourceSystem.Trim(),
            sourceRecordId.Trim());
    }
}
