namespace CriticalAlerts.Application.Dispatch;

public static class SimulationDispatchEnvironmentGuard
{
    public static void EnsureAllowed(string? environmentName, bool enabled)
    {
        if (enabled
            && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Simulation dispatch cannot be enabled outside Development or Test; startup was rejected.");
        }
    }

    public static bool IsSimulationEnvironment(string? environmentName)
        => string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase);
}
