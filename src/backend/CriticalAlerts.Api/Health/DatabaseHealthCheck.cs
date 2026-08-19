using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CriticalAlerts.Api.Health;

/// <summary>Reports whether the configured local PostgreSQL dependency is reachable.</summary>
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IConfiguration configuration;

    /// <summary>Creates the health check with safe application configuration.</summary>
    public DatabaseHealthCheck(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("CriticalAlerts");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy("database-configuration-missing");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("database-available");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not expose connection details, SQL, provider exceptions, or database content.
            return HealthCheckResult.Unhealthy("database-unavailable");
        }
    }
}
