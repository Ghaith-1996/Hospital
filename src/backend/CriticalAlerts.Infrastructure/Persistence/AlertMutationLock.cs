using CriticalAlerts.Domain;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Persistence;

internal static class AlertMutationLock
{
    // Hold the alert row until commit so recipient writes and lifecycle decisions
    // observe each other even when a response does not update the alert's xmin.
    public static Task AcquireAsync(
        CriticalAlertsDbContext db,
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Alert mutation locking requires an active transaction.");
        }

        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM alerts WHERE organization_id = {organizationId.Value} AND id = {alertId.Value} FOR UPDATE",
            cancellationToken);
    }
}
