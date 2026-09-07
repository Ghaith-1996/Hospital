using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed class DatabaseClock(CriticalAlertsDbContext db)
{
    // Call after locks: PostgreSQL now() is the transaction start instant and can be stale.
    public async Task<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        var instant = await db.Database.SqlQuery<DateTime>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        return new DateTimeOffset(instant);
    }
}
