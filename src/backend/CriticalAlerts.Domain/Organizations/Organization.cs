namespace CriticalAlerts.Domain.Organizations;

public sealed class Organization
{
    private Organization()
    {
        Name = string.Empty;
    }

    private Organization(OrganizationId id, string name, bool isSimulation, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        IsSimulation = isSimulation;
        CreatedAtUtc = createdAtUtc;
    }

    public OrganizationId Id { get; private set; }

    public string Name { get; private set; }

    public bool IsSimulation { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Organization CreateSimulation(OrganizationId id, string name, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Organization name is required.");
        }

        return new Organization(id, name.Trim(), isSimulation: true, UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
