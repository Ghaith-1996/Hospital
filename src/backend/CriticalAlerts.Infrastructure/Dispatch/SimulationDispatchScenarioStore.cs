using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class SimulationDispatchScenarioStore(CriticalAlertsDbContext db) : ISimulationDispatchScenarioStore
{
    public async Task<SimulationDispatchScenario> GetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        var setting = await db.SimulationDispatchScenarioSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.Channel == channel,
                cancellationToken);
        return setting?.Scenario ?? SimulationDispatchScenario.ImmediateSuccess;
    }

    public async Task SetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        SimulationDispatchScenario scenario,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var setting = await db.SimulationDispatchScenarioSettings
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.Channel == channel,
                cancellationToken);
        if (setting is null)
        {
            db.SimulationDispatchScenarioSettings.Add(SimulationDispatchScenarioSetting.Create(
                SimulationDispatchScenarioSettingId.New(),
                organizationId,
                channel,
                scenario,
                updatedByUserId,
                updatedAtUtc));
        }
        else
        {
            setting.Update(scenario, updatedByUserId, updatedAtUtc);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetAsync(
        OrganizationId organizationId,
        NotificationChannel channel,
        UserId updatedByUserId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        _ = updatedByUserId;
        var setting = await db.SimulationDispatchScenarioSettings
            .SingleOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.Channel == channel,
                cancellationToken);
        if (setting is null)
        {
            return;
        }

        db.SimulationDispatchScenarioSettings.Remove(setting);
        await db.SaveChangesAsync(cancellationToken);
    }
}
