using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class DatabaseOperations
{
    private static readonly string[] AllowedEnvironments = ["Development", "Test"];

    public static void EnsureEnvironmentAllowed(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment) || !AllowedEnvironments.Contains(environment, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Database migrate/reset is restricted to Development or Test; environment '{environment}' was rejected and no database was changed.");
        }
    }

    public static CriticalAlertsDbContext CreateContext(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string is required.");
        }

        var options = new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CriticalAlertsDbContext(options);
    }

    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public static async Task ResetDemoAsync(
        string connectionString,
        string environment,
        string dataProtectionKey,
        CancellationToken cancellationToken = default)
    {
        EnsureEnvironmentAllowed(environment);
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        var seeder = new DemoDataSeeder(db, dataProtectionKey);
        await seeder.SeedAsync(cancellationToken);
    }
}
