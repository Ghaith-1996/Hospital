namespace CriticalAlerts.Domain.Directory;

public sealed class ContactEndpoint
{
    private ContactEndpoint()
    {
        SimulationLabel = string.Empty;
        SourceSystem = string.Empty;
        SourceRecordId = string.Empty;
    }

    private ContactEndpoint(
        ContactEndpointId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        ContactEndpointKind kind,
        ProtectedValue protectedValue,
        string simulationLabel,
        bool isPrimary,
        bool isActive,
        string sourceSystem,
        string sourceRecordId)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        Kind = kind;
        ProtectedValue = protectedValue;
        SimulationLabel = simulationLabel;
        IsPrimary = isPrimary;
        IsActive = isActive;
        SourceSystem = sourceSystem;
        SourceRecordId = sourceRecordId;
    }

    public ContactEndpointId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public ContactEndpointKind Kind { get; private set; }

    public ProtectedValue ProtectedValue { get; private set; } = null!;

    public string SimulationLabel { get; private set; }

    public bool IsPrimary { get; private set; }

    public bool IsActive { get; private set; }

    public string SourceSystem { get; private set; }

    public string SourceRecordId { get; private set; }

    public static ContactEndpoint Create(
        ContactEndpointId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        ContactEndpointKind kind,
        ProtectedValue protectedValue,
        string simulationLabel,
        bool isPrimary,
        string sourceSystem,
        string sourceRecordId)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        if (string.IsNullOrWhiteSpace(simulationLabel))
        {
            throw new DomainException("Contact endpoints require a non-sensitive simulation label.");
        }

        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(sourceRecordId))
        {
            throw new DomainException("Contact endpoints require a source system and source record.");
        }

        return new ContactEndpoint(
            id,
            organizationId,
            practitionerId,
            kind,
            protectedValue,
            simulationLabel.Trim(),
            isPrimary,
            isActive: true,
            sourceSystem.Trim(),
            sourceRecordId.Trim());
    }
}
