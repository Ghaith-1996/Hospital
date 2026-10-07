using CriticalAlerts.Infrastructure.Persistence;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class DatabaseCommandHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (args is ["database", "validate-restore"])
        {
            try
            {
                var connection = ResolveConnectionString();
                var target = new Npgsql.NpgsqlConnectionStringBuilder(connection);
                RestoreValidation.EnsureSafeTarget(environment, target.Host, target.Database);
                var result = await RestoreValidation.ValidateAsync(connection, environment!);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
                return 0;
            }
            catch (Exception)
            {
                Console.Error.WriteLine("Restore validation failed. Check the simulation environment, schema, and relational invariants.");
                return 1;
            }
        }
        if (string.IsNullOrWhiteSpace(environment))
        {
            environment = "Development";
        }

        DatabaseOperations.EnsureEnvironmentAllowed(environment);

        var connectionString = ResolveConnectionString();
        if (args is ["database", "migrate"])
        {
            var key = Environment.GetEnvironmentVariable("CRITICAL_ALERTS_DATA_PROTECTION_KEY");
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    "CRITICAL_ALERTS_DATA_PROTECTION_KEY is required to apply protected-data migrations.");
            }

            await DatabaseOperations.MigrateAsync(connectionString, key);
            Console.WriteLine("Database migrations applied. No provider or hospital integration was configured.");
            return 0;
        }

        if (args is ["database", "reset-demo", "--confirm-demo-reset"])
        {
            var key = Environment.GetEnvironmentVariable("CRITICAL_ALERTS_DATA_PROTECTION_KEY");
            var escalationStepDelay = ResolveEscalationStepDelay();
            await DatabaseOperations.ResetDemoAsync(
                connectionString, environment, key ?? string.Empty, confirmReset: true, escalationStepDelay: escalationStepDelay);
            Console.WriteLine("Demo database was reset with fictional simulation data only.");
            if (escalationStepDelay is not null)
            {
                Console.WriteLine($"DEMO escalation step delay was shortened to {escalationStepDelay.Value.TotalSeconds:0} seconds for automated tests.");
            }

            return 0;
        }

        throw new InvalidOperationException("Supported database commands are 'database migrate' and 'database reset-demo --confirm-demo-reset'.");
    }

    // Automated tests only: lets the system E2E seed a short DEMO escalation deadline instead of waiting the
    // real 60 seconds. Unset keeps the documented 60-second step. Only reachable from the Development/Test
    // reset-demo command.
    private static TimeSpan? ResolveEscalationStepDelay()
    {
        const string variable = "SimulationEscalation__DemoStepDelaySeconds";
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (!int.TryParse(configured, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 1 or > 300)
        {
            throw new InvalidOperationException($"{variable} must be a whole number of seconds from 1 to 300; no database was changed.");
        }

        return TimeSpan.FromSeconds(seconds);
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
