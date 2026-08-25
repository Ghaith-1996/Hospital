namespace CriticalAlerts.Domain.Simulation;

/// <summary>
/// Development/Test synthetic-data rules. This is a <c>SimulationEnvironmentPolicy</c>,
/// not a <c>HealthcareDomainInvariant</c>. Production identifier formats are
/// <c>REQUIRES_HOSPITAL_DECISION</c> and must not be blocked by this prefix check.
/// </summary>
public static class SimulationEnvironmentPolicy
{
    public const string SyntheticPrefix = "SIM-";
    public const string SyntheticPatientReferencePrefix = SyntheticPrefix;

    public static string RequireSyntheticPatientReference(string value)
        => RequireSyntheticPrefix(value, "patient reference");

    public static string RequireSyntheticPrefix(string value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(SyntheticPrefix, StringComparison.Ordinal))
        {
            throw new DomainException($"Simulation {purpose} values require a synthetic SIM- prefix. This is a SimulationEnvironmentPolicy, not a healthcare domain invariant.");
        }

        return value.Trim();
    }

    public static bool HasSyntheticPrefix(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.StartsWith(SyntheticPrefix, StringComparison.Ordinal);
}

