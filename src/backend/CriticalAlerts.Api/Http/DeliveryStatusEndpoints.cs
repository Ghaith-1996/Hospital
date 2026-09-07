using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Api.Http;

internal static class DeliveryStatusEndpoints
{
    public static void MapDeliveryStatusEndpoints(this WebApplication app)
    {
        app.MapGet(
                $"{ApiRouteConstants.BasePath}/alerts/{{alertId:guid}}/delivery",
                Get)
            .RequireAuthorization(AuthorizationPolicies.AlertDeliveryReader)
            .RequireRateLimiting("api")
            .Produces<DeliveryStatusView>().WithApiErrors().Produces(404);
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        IDeliveryStatusQueryService status,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganization(principal, out var organizationId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");
        }

        var view = await status.GetAsync(organizationId, new AlertId(alertId), cancellationToken);
        return view is null ? Results.NotFound() : Results.Ok(view);
    }

    private static bool TryGetOrganization(ClaimsPrincipal principal, out OrganizationId organizationId)
    {
        organizationId = default;
        return Guid.TryParse(
                principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId),
                out var parsedOrganization)
            && (organizationId = new OrganizationId(parsedOrganization)).Value != Guid.Empty;
    }
}
