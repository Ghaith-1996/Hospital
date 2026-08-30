namespace CriticalAlerts.Domain.Delivery;

public sealed class SimulationDispatchScenarioSetting
{
    private SimulationDispatchScenarioSetting()
    {
    }

    private SimulationDispatchScenarioSetting(
        SimulationDispatchScenarioSettingId id,
        OrganizationId organizationId,
        NotificationChannel channel,
        SimulationDispatchScenario scenario,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        Channel = channel;
        Scenario = scenario;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public SimulationDispatchScenarioSettingId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public SimulationDispatchScenario Scenario { get; private set; }

    public UserId UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static SimulationDispatchScenarioSetting Create(
        SimulationDispatchScenarioSettingId id,
        OrganizationId organizationId,
        NotificationChannel channel,
        SimulationDispatchScenario scenario,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        Validate(channel, scenario);
        return new SimulationDispatchScenarioSetting(
            id,
            organizationId,
            channel,
            scenario,
            updatedByUserId,
            UtcInstant.Require(updatedAtUtc, nameof(updatedAtUtc)));
    }

    public void Update(
        SimulationDispatchScenario scenario,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        Validate(Channel, scenario);
        Scenario = scenario;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = UtcInstant.Require(updatedAtUtc, nameof(updatedAtUtc));
    }

    private static void Validate(NotificationChannel channel, SimulationDispatchScenario scenario)
    {
        if (!Enum.IsDefined(channel) || !Enum.IsDefined(scenario))
        {
            throw new DomainException("Simulation dispatch settings require a supported channel and scenario.");
        }
    }
}
