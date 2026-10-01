using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Api.Http;

internal static class EndpointHelpers
{
    public static bool TryGetActor(ClaimsPrincipal principal, out UserId userId, out OrganizationId organizationId)
    {
        userId = default;
        organizationId = default;
        var userValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizationValue = principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId);
        if (!Guid.TryParse(userValue, out var parsedUser) || !Guid.TryParse(organizationValue, out var parsedOrganization))
        {
            return false;
        }

        userId = new UserId(parsedUser);
        organizationId = new OrganizationId(parsedOrganization);
        return true;
    }

    public static IResult Unauthorized()
        => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");

    public static IResult NotFound()
        => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "alert-not-found");

    public static string CorrelationId(HttpContext httpContext)
        => httpContext.Response.Headers["X-Correlation-ID"].ToString();
}
