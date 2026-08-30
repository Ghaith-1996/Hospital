using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Application.Dispatch;

public interface ISimulationDispatchScenarioStore
{
    Task<SimulationDispatchScenario> GetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        CancellationToken cancellationToken);

    Task SetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        SimulationDispatchScenario scenario,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);

    Task ResetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken);
}
