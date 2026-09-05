using CriticalAlerts.Application.Identity;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CriticalAlerts.Api.Authentication;

internal static class DevelopmentAuthenticationServiceCollectionExtensions
{
    public const string CookieName = ".CriticalAlerts.DevAuth";

    public static IServiceCollection AddDevelopmentAuthentication(this IServiceCollection services, string environmentName, bool enabled)
    {
        DevelopmentAuthenticationGuard.EnsureAllowed(environmentName, enabled);

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.Events.OnRedirectToLogin = context => WriteProblemAsync(context.Response, StatusCodes.Status401Unauthorized, "Unauthorized", "authentication-required");
                options.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(context.Response, StatusCodes.Status403Forbidden, "Forbidden", "authorization-failed");
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthorizationPolicies.Operator, policy => policy.RequireRole(AuthorizationRoles.Operator));
            options.AddPolicy(AuthorizationPolicies.Physician, policy => policy.RequireRole(AuthorizationRoles.Physician));
            options.AddPolicy(AuthorizationPolicies.ClinicalSupervisor, policy => policy.RequireRole(AuthorizationRoles.ClinicalSupervisor));
            options.AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(AuthorizationRoles.Administrator));
            options.AddPolicy(AuthorizationPolicies.Practitioner, policy => policy.RequireRole(AuthorizationRoles.Practitioner));
            options.AddPolicy(
                AuthorizationPolicies.DirectoryReader,
                policy => policy.RequireRole(
                    AuthorizationRoles.Operator,
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.DirectoryAdministrator,
                    AuthorizationRoles.Auditor,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.DirectoryAdministrator,
                policy => policy.RequireRole(
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.DirectoryAdministrator,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.AlertDraftEditor,
                policy => policy.RequireRole(
                    AuthorizationRoles.Operator,
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.ClinicalSupervisor,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.DispatchScenarioAdministrator,
                policy => policy.RequireRole(
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.IntegrationAdministrator,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.AlertDeliveryReader,
                policy => policy.RequireRole(
                    AuthorizationRoles.Operator,
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.ClinicalSupervisor,
                    AuthorizationRoles.Auditor,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.PractitionerAlertResponder,
                policy => policy.RequireRole(AuthorizationRoles.Practitioner, AuthorizationRoles.Physician));
            options.AddPolicy(
                AuthorizationPolicies.AlertLiveReader,
                policy => policy.RequireRole(
                    AuthorizationRoles.Operator,
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.ClinicalSupervisor,
                    AuthorizationRoles.Auditor,
                    AuthorizationRoles.SystemAdministrator));
            options.AddPolicy(
                AuthorizationPolicies.AlertLifecycleOperator,
                policy => policy.RequireRole(
                    AuthorizationRoles.Operator,
                    AuthorizationRoles.Administrator,
                    AuthorizationRoles.ClinicalSupervisor,
                    AuthorizationRoles.SystemAdministrator));
        });

        return services;
    }

    private static async Task WriteProblemAsync(HttpResponse response, int statusCode, string title, string detail)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";
        await response.WriteAsJsonAsync(new { type = "about:blank", title, status = statusCode, detail });
    }
}
