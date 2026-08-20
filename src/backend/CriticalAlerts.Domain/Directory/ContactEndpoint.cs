namespace CriticalAlerts.Domain.Directory;

public sealed class ContactEndpoint
{
    private ContactEndpoint()
    {
        SimulationLabel = string.Empty;
    }

    private ContactEndpoint(
        ContactEndpointId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        ContactEndpointKind kind,
        ProtectedValue protectedValue,
        string simulationLabel,
        bool isPrimary,
        bool isActive)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        Kind = kind;
        ProtectedValue = protectedValue;
        SimulationLabel = simulationLabel;
        IsPrimary = isPrimary;
        IsActive = isActive;
    }

    public ContactEndpointId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public ContactEndpointKind Kind { get; private set; }

    public ProtectedValue ProtectedValue { get; private set; } = null!;

    public string SimulationLabel { get; private set; }

    public bool IsPrimary { get; private set; }

    public bool IsActive { get; private set; }

    public static ContactEndpoint Create(
        ContactEndpointId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        ContactEndpointKind kind,
        ProtectedValue protectedValue,
        string simulationLabel,
        bool isPrimary)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        if (string.IsNullOrWhiteSpace(simulationLabel))
        {
            throw new DomainException("Contact endpoints require a non-sensitive simulation label.");
        }

        return new ContactEndpoint(
            id,
            organizationId,
            practitionerId,
            kind,
            protectedValue,
            simulationLabel.Trim(),
            isPrimary,
            isActive: true);
    }
}
