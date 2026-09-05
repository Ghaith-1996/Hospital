namespace CriticalAlerts.Application.Responses;

public static class SimulationResponseEnvironmentGuard
{
    public static void EnsureAllowed(string? environmentName, bool enabled)
    {
        if (enabled && !IsSimulationEnvironment(environmentName))
        {
            throw new InvalidOperationException(
                "Simulation responses cannot be enabled outside Development or Test; startup was rejected.");
        }
    }

    public static bool IsSimulationEnvironment(string? environmentName)
        => string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
