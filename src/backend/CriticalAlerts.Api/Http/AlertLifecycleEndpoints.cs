using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Api.Http;

internal static class AlertLifecycleEndpoints
{
    public static void MapAlertLifecycleEndpoints(this WebApplication app, string environmentName)
    {
        if (!SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environmentName))
        {
            return;
        }

        var group = app.MapGroup($"{ApiRouteConstants.BasePath}/alerts")
            .RequireAuthorization(AuthorizationPolicies.AlertLifecycleOperator)
            .RequireRateLimiting("api");
        group.MapPost("/{alertId:guid}/resolve", Resolve);
        group.MapPost("/{alertId:guid}/cancel", Cancel);
    }

    private static Task<IResult> Resolve(
        ClaimsPrincipal principal,
        IAlertLifecycleService lifecycle,
        HttpContext httpContext,
        Guid alertId,
        AlertLifecycleActionRequest? request,
        CancellationToken cancellationToken)
        => ExecuteAsync(principal, lifecycle, httpContext, alertId, request, resolve: true, cancellationToken);

    private static Task<IResult> Cancel(
        ClaimsPrincipal principal,
        IAlertLifecycleService lifecycle,
        HttpContext httpContext,
        Guid alertId,
        AlertLifecycleActionRequest? request,
        CancellationToken cancellationToken)
        => ExecuteAsync(principal, lifecycle, httpContext, alertId, request, resolve: false, cancellationToken);

    private static async Task<IResult> ExecuteAsync(
        ClaimsPrincipal principal,
        IAlertLifecycleService lifecycle,
        HttpContext httpContext,
        Guid alertId,
        AlertLifecycleActionRequest? request,
        bool resolve,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "authentication-required");
        }

        if (request is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["request"] = ["An exact confirmed alert version is required."] },
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid alert lifecycle action");
        }

        try
        {
            var result = resolve
                ? await lifecycle.ResolveAsync(
                    organizationId,
                    userId,
                    CorrelationId(httpContext),
                    new AlertId(alertId),
                    request,
                    httpContext.Request.Headers["Idempotency-Key"].ToString(),
                    cancellationToken)
                : await lifecycle.CancelAsync(
                    organizationId,
                    userId,
                    CorrelationId(httpContext),
                    new AlertId(alertId),
                    request,
                    httpContext.Request.Headers["Idempotency-Key"].ToString(),
                    cancellationToken);
            return result is null
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "alert-not-found")
                : Results.Ok(result);
        }
        catch (AlertLifecycleValidationException exception)
        {
            return exception.Code is "alert-version-stale"
                or "alert-state-conflict"
                or "responsibility-required"
                or "idempotency-conflict"
                or "lifecycle-in-progress"
                or "lifecycle-conflict"
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Alert lifecycle conflict", detail: exception.Code)
                : Results.ValidationProblem(
                    new Dictionary<string, string[]> { [exception.Code] = [exception.Message] },
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid alert lifecycle action");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Alert lifecycle conflict",
                detail: "lifecycle-conflict");
        }
    }

    private static string CorrelationId(HttpContext httpContext)
        => httpContext.Response.Headers["X-Correlation-ID"].ToString();

    private static bool TryGetActor(
        ClaimsPrincipal principal,
        out UserId userId,
        out OrganizationId organizationId)
    {
        userId = default;
        organizationId = default;
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser)
            || !Guid.TryParse(principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId), out var parsedOrganization))
        {
            return false;
        }

        userId = new UserId(parsedUser);
        organizationId = new OrganizationId(parsedOrganization);
        return true;
    }
}
