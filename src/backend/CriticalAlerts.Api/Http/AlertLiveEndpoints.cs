using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Api.Http;

internal static class AlertLiveEndpoints
{
    public static void MapAlertLiveEndpoints(
        this WebApplication app,
        string environmentName,
        bool enabled)
    {
        if (!enabled || !SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environmentName))
        {
            return;
        }

        app.MapGet("/api/alerts/{alertId:guid}/live", Get)
            .RequireAuthorization(AuthorizationPolicies.AlertLiveReader);
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        IAlertLiveQueryService live,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(
                principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId),
                out var organizationId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "authentication-required");
        }

        var result = await live.GetAsync(
            new OrganizationId(organizationId),
            new AlertId(alertId),
            cancellationToken);
        return result is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found",
                detail: "alert-not-found")
            : Results.Ok(result);
    }
}
