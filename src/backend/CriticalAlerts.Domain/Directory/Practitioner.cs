namespace CriticalAlerts.Domain.Directory;

public sealed class Practitioner
{
    private Practitioner()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        SimulationCode = string.Empty;
        Specialty = string.Empty;
    }

    private Practitioner(
        PractitionerId id,
        OrganizationId organizationId,
        string firstName,
        string lastName,
        string simulationCode,
        string specialty,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        FirstName = firstName;
        LastName = lastName;
        SimulationCode = simulationCode;
        Specialty = specialty;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
    }

    public PractitionerId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string SimulationCode { get; private set; }

    public string Specialty { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Practitioner Create(
        PractitionerId id,
        OrganizationId organizationId,
        string firstName,
        string lastName,
        string simulationCode,
        string specialty,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(simulationCode))
        {
            throw new DomainException("Practitioners require a name and simulation code.");
        }

        return new Practitioner(
            id,
            organizationId,
            firstName.Trim(),
            lastName.Trim(),
            simulationCode.Trim(),
            specialty.Trim(),
            isActive,
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }

    public void Deactivate() => IsActive = false;
}
