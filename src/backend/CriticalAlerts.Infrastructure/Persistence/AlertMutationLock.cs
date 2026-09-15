using CriticalAlerts.Domain;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Persistence;

internal static class AlertMutationLock
{
    // Called only after the shared alert lock. A clock correction must produce a safe retry,
    // never evidence predating a committed selection, response, lifecycle or worker mutation.
    public static async Task<DateTimeOffset?> TryGetMutationTimeAsync(CriticalAlertsDbContext db,
        OrganizationId organizationId, AlertId alertId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Mutation time requires the alert transaction lock.");
        var now = await new Escalation.DatabaseClock(db).GetUtcNowAsync(cancellationToken);
        var latest = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT MAX(e.instant) AS "Value" FROM (
                SELECT updated_at_utc AS instant FROM alerts WHERE organization_id = {organizationId.Value} AND id = {alertId.Value}
                UNION ALL SELECT selected_at_utc FROM alert_recipient_selections WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value}
                UNION ALL SELECT COALESCE(updated_at_utc, started_at_utc) FROM escalation_runs WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value}
                UNION ALL SELECT occurred_at_utc FROM recipient_responses WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value}
                UNION ALL SELECT accepted_at_utc FROM responsibility_assignments WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value}
                UNION ALL SELECT GREATEST(requested_at_utc, submitted_at_utc, delivered_at_utc, failed_at_utc, opened_at_utc) FROM delivery_attempts WHERE organization_id = {organizationId.Value} AND alert_id = {alertId.Value}
            ) e
            """).SingleAsync(cancellationToken);
        return latest > now ? null : now;
    }

    public static async Task<bool> TryAcquireAsync(CriticalAlertsDbContext db, OrganizationId organizationId,
        AlertId alertId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Alert mutation locking requires an active transaction.");
        var ids = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM alerts WHERE organization_id = {organizationId.Value} AND id = {alertId.Value} FOR UPDATE SKIP LOCKED")
            .ToArrayAsync(cancellationToken);
        return ids.Length == 1;
    }

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
