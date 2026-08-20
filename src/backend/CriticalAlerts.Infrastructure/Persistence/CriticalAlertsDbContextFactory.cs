using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CriticalAlerts.Infrastructure.Persistence;

public sealed class CriticalAlertsDbContextFactory : IDesignTimeDbContextFactory<CriticalAlertsDbContext>
{
    public CriticalAlertsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CriticalAlerts")
            ?? "Host=127.0.0.1;Port=55432;Database=critical_alerts_dev;Username=critical_alerts_dev;Password=unset";
        var options = new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CriticalAlertsDbContext(options);
    }
}
