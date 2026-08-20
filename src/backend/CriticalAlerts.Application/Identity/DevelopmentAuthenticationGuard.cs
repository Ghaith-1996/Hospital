namespace CriticalAlerts.Application.Identity;

public static class DevelopmentAuthenticationGuard
{
    public static void EnsureAllowed(string? environment, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        if (!IsSimulationEnvironment(environment))
        {
            throw new InvalidOperationException(
                "Development authentication cannot be enabled outside Development or Test; no hospital identity provider was configured.");
        }
    }

    public static bool IsSimulationEnvironment(string? environment)
        => environment is "Development" or "Test";
}
