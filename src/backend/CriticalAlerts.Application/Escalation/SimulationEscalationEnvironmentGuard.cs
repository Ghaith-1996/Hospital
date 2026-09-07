namespace CriticalAlerts.Application.Escalation;

public static class SimulationEscalationEnvironmentGuard
{
    public static void EnsureAllowed(string? environmentName, bool enabled)
    {
        if (enabled && !string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environmentName, "Test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Simulation escalation cannot be enabled outside Development or Test; startup was rejected.");
    }
}
