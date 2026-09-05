using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class DatabaseOperations
{
    private static readonly string[] AllowedEnvironments = ["Development", "Test"];
    private static readonly string[] AllowedDemoDatabasePrefixes =
    [
        "critical_alerts_dev",
        "critical_alerts_test",
        "critical_alerts_demo",
    ];

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
        await SensitiveDataMigration.CompleteAsync(db, null, cancellationToken);
    }

    public static async Task MigrateAsync(
        string connectionString,
        string dataProtectionKey,
        CancellationToken cancellationToken = default)
    {
        await using var db = CreateContext(connectionString);
        await db.Database.MigrateAsync(cancellationToken);
        await SensitiveDataMigration.CompleteAsync(db, dataProtectionKey, cancellationToken);
    }

    public static async Task ResetDemoAsync(
        string connectionString,
        string environment,
        string dataProtectionKey,
        bool confirmReset,
        CancellationToken cancellationToken = default)
    {
        EnsureEnvironmentAllowed(environment);
        EnsureDemoResetTarget(connectionString, confirmReset);
        await using var db = CreateContext(connectionString);
        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await SensitiveDataMigration.CompleteAsync(db, dataProtectionKey, cancellationToken);
        var seeder = new DemoDataSeeder(db, dataProtectionKey);
        await seeder.SeedAsync(cancellationToken);
    }

    public static void EnsureDemoResetTarget(string connectionString, bool confirmReset)
    {
        if (!confirmReset)
        {
            throw new InvalidOperationException(
                "Demo reset requires the explicit confirmation flag; no database was changed.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "Demo reset requires a valid local PostgreSQL connection string; no database was changed.",
                exception);
        }

        var hosts = (builder.Host ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => !IsLoopbackHost(host)))
        {
            throw new InvalidOperationException(
                "Demo reset is restricted to a loopback PostgreSQL host; no database was changed.");
        }

        var databaseName = builder.Database ?? string.Empty;
        if (!IsAllowedDemoDatabaseName(databaseName))
        {
            throw new InvalidOperationException(
                "Demo reset is restricted to a critical_alerts_dev, critical_alerts_test, or critical_alerts_demo local database; no database was changed.");
        }
    }

    private static bool IsAllowedDemoDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName)
            || databaseName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return false;
        }

        return AllowedDemoDatabasePrefixes.Any(prefix =>
            string.Equals(databaseName, prefix, StringComparison.OrdinalIgnoreCase)
            || databaseName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLoopbackHost(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal)
            || string.Equals(host, "[::1]", StringComparison.Ordinal);
}
