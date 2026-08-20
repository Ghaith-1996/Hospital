using CriticalAlerts.Infrastructure.Persistence;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class DatabaseCommandHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environment))
        {
            environment = "Development";
        }

        DatabaseOperations.EnsureEnvironmentAllowed(environment);

        var connectionString = ResolveConnectionString();
        if (args is ["database", "migrate"])
        {
            await DatabaseOperations.MigrateAsync(connectionString);
            Console.WriteLine("Phase 2 migrations applied. No provider or hospital integration was configured.");
            return 0;
        }

        if (args is ["database", "reset-demo"])
        {
            var key = Environment.GetEnvironmentVariable("CRITICAL_ALERTS_DATA_PROTECTION_KEY");
            await DatabaseOperations.ResetDemoAsync(connectionString, environment, key ?? string.Empty);
            Console.WriteLine("Phase 2 demo database was reset with fictional simulation data only.");
            return 0;
        }

        throw new InvalidOperationException("Supported database commands are 'database migrate' and 'database reset-demo'.");
    }

    private static string ResolveConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__CriticalAlerts");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var database = Environment.GetEnvironmentVariable("POSTGRES_DB");
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER");
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "55432";
        if (string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set ConnectionStrings__CriticalAlerts or POSTGRES_* values in the ignored local .env file.");
        }

        return $"Host=127.0.0.1;Port={port};Database={database};Username={user};Password={password}";
    }
}
