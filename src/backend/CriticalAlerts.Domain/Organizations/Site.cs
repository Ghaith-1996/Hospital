using CriticalAlerts.Domain.Simulation;

namespace CriticalAlerts.Domain.Organizations;

public sealed class Site
{
    private Site()
    {
        Name = string.Empty;
        SimulationCode = string.Empty;
    }

    private Site(SiteId id, OrganizationId organizationId, string name, string simulationCode, DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        SimulationCode = simulationCode;
        CreatedAtUtc = createdAtUtc;
    }

    public SiteId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string SimulationCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Site Create(SiteId id, OrganizationId organizationId, string name, string simulationCode, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Site name is required.");
        }

        return new Site(
            id,
            organizationId,
            name.Trim(),
            SimulationEnvironmentPolicy.RequireSyntheticPrefix(simulationCode, "site code"),
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
