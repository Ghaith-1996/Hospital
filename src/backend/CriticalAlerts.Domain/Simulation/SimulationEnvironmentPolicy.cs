namespace CriticalAlerts.Domain.Simulation;

/// <summary>
/// Development/Test synthetic-data rules. This is a <c>SimulationEnvironmentPolicy</c>,
/// not a <c>HealthcareDomainInvariant</c>. Production patient-reference formats are
/// <c>REQUIRES_HOSPITAL_DECISION</c> and must not be blocked by this prefix check.
/// </summary>
public static class SimulationEnvironmentPolicy
{
    public const string SyntheticPatientReferencePrefix = "SIM-";

    public static string RequireSyntheticPatientReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(SyntheticPatientReferencePrefix, StringComparison.Ordinal))
        {
            throw new DomainException("Simulation alerts require a synthetic SIM- patient reference. This is a SimulationEnvironmentPolicy, not a healthcare domain invariant.");
        }

        return value.Trim();
    }
}
