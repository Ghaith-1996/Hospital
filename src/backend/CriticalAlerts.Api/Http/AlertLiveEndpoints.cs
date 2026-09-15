using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using Microsoft.AspNetCore.Authorization;

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

        app.MapGet($"{ApiRouteConstants.BasePath}/alerts/{{alertId:guid}}/live", Get)
            .RequireAuthorization(AuthorizationPolicies.AlertLiveReader)
            .RequireRateLimiting("api")
            .WithReadOnlyProjection<AlertLiveView>(
                "Returns a read-only, organization-scoped live simulation projection. PostgreSQL UTC owns escalation timing; polling never advances escalation.",
                404);
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        IAlertLiveQueryService live,
        IAuthorizationService authorization,
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

        var canOperateLifecycle = (await authorization.AuthorizeAsync(
            principal,
            AuthorizationPolicies.AlertLifecycleOperator)).Succeeded;
        var result = await live.GetAsync(
            new OrganizationId(organizationId),
            new AlertId(alertId),
            canOperateLifecycle,
            cancellationToken);
        return result is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found",
                detail: "alert-not-found")
            : Results.Ok(result);
    }
}
