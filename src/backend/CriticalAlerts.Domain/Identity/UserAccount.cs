namespace CriticalAlerts.Domain.Identity;

public sealed class UserAccount
{
    private UserAccount()
    {
        DisplayName = string.Empty;
        SimulationHandle = string.Empty;
    }

    private UserAccount(
        UserId id,
        OrganizationId organizationId,
        string displayName,
        string simulationHandle,
        bool isActive,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        DisplayName = displayName;
        SimulationHandle = simulationHandle;
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc;
    }

    public UserId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string DisplayName { get; private set; }

    public string SimulationHandle { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static UserAccount CreateSimulation(
        UserId id,
        OrganizationId organizationId,
        string displayName,
        string simulationHandle,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(simulationHandle))
        {
            throw new DomainException("Simulation users require a display name and handle.");
        }

        return new UserAccount(
            id,
            organizationId,
            displayName.Trim(),
            simulationHandle.Trim(),
            isActive: true,
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }
}
