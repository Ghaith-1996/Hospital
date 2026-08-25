namespace CriticalAlerts.Application.Identity;

public static class AuthorizationRoles
{
    public const string Operator = "Operator";
    public const string Administrator = "Administrator";
    public const string Practitioner = "Practitioner";
}

public static class AuthorizationPolicies
{
    public const string Operator = "Operator";
    public const string Administrator = "Administrator";
    public const string Practitioner = "Practitioner";
    public const string DirectoryReader = "DirectoryReader";
    public const string DirectoryAdministrator = "DirectoryAdministrator";
}

public static class AuthenticationClaimTypes
{
    public const string OrganizationId = "organization_id";
    public const string SimulationHandle = "simulation_handle";
    public const string AuthenticationMode = "auth_mode";
    public const string DevelopmentMode = "development";
}
