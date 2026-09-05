namespace CriticalAlerts.Application.Identity;

public static class AuthorizationRoles
{
    public const string Operator = "Operator";
    public const string Physician = "Physician";
    public const string ClinicalSupervisor = "ClinicalSupervisor";
    public const string DirectoryAdministrator = "DirectoryAdministrator";
    public const string IntegrationAdministrator = "IntegrationAdministrator";
    public const string Auditor = "Auditor";
    public const string SystemAdministrator = "SystemAdministrator";

    // Kept as simulation compatibility roles while the production role map remains undecided.
    public const string Administrator = "Administrator";
    public const string Practitioner = "Practitioner";
}

public static class AuthorizationPolicies
{
    public const string Operator = "Operator";
    public const string Physician = "Physician";
    public const string ClinicalSupervisor = "ClinicalSupervisor";
    public const string Administrator = "Administrator";
    public const string Practitioner = "Practitioner";
    public const string DirectoryReader = "DirectoryReader";
    public const string DirectoryAdministrator = "DirectoryAdministrator";
    public const string AlertDraftEditor = "AlertDraftEditor";
    public const string DispatchScenarioAdministrator = "DispatchScenarioAdministrator";
    public const string AlertDeliveryReader = "AlertDeliveryReader";
    public const string PractitionerAlertResponder = "PractitionerAlertResponder";
    public const string AlertLiveReader = "AlertLiveReader";
    public const string AlertLifecycleOperator = "AlertLifecycleOperator";
}

public static class AuthenticationClaimTypes
{
    public const string OrganizationId = "organization_id";
    public const string SimulationHandle = "simulation_handle";
    public const string AuthenticationMode = "auth_mode";
    public const string DevelopmentMode = "development";
}
