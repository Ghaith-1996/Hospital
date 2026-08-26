namespace CriticalAlerts.Application.Directory;

/// <summary>
/// The allowlist for the fictional CSV fixture. It is a simulation input policy,
/// not the future hospital directory's canonical practitioner vocabulary.
/// </summary>
public static class SimulationDirectoryCatalog
{
    private static readonly IReadOnlySet<string> AllowedPractitioners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Maya Chen",
        "Rowan Patel",
        "Jules Martin",
        "Avery Brooks",
        "Samira Nguyen",
        "Jordan Martin",
        "Casey Okonkwo",
        "Riley Sato",
        "Quinn Alvarez",
        "Harper Singh",
        "Taylor Kim",
        "Cameron Wright",
    };

    private static readonly IReadOnlySet<string> AllowedSpecialties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Emergency",
        "Medicine",
        "Surgery",
        "Cardiology",
        "Neurology",
        "Pediatrics",
    };

    public static bool IsAllowedPractitioner(string firstName, string lastName)
        => AllowedPractitioners.Contains($"{firstName.Trim()} {lastName.Trim()}");

    public static bool IsAllowedSpecialty(string specialty)
        => AllowedSpecialties.Contains(specialty.Trim());
}
