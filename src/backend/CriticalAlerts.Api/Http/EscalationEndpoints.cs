using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Escalation;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Api.Http;

internal static class EscalationEndpoints
{
    public static void MapEscalationEndpoints(this WebApplication app, string environmentName)
    {
        if (!SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environmentName))
        {
            return;
        }

        var group = app.MapGroup($"{ApiRouteConstants.BasePath}/alerts")
            .RequireAuthorization(AuthorizationPolicies.AlertLifecycleOperator)
            .RequireRateLimiting("api");
        group.MapPost("/{alertId:guid}/escalation/pause", Pause).WithDescription("Development/Test DEMO only. Exact version and ReasonCode OperatorReview or ManualCoordination required. Saves remaining delay. A retained replay returns the original result.").WithIdempotencyHeader(100).Produces<EscalationOverrideResult>().WithApiErrors(400, 404, 409).Produces(413);
        group.MapPost("/{alertId:guid}/escalation/resume", Resume).WithDescription("Development/Test DEMO only. Exact version and ReasonCode ReadyToResume required. Restores saved delay using database UTC time. Same idempotency key across pause/resume conflicts.").WithIdempotencyHeader(100).Produces<EscalationOverrideResult>().WithApiErrors(400, 404, 409).Produces(413);
    }

    private static Task<IResult> Pause(
        ClaimsPrincipal principal,
        IEscalationOverrideService overrides,
        HttpContext httpContext,
        Guid alertId,
        EscalationOverrideRequest? request,
        CancellationToken cancellationToken)
        => ExecuteAsync(principal, overrides, httpContext, alertId, request, pause: true, cancellationToken);

    private static Task<IResult> Resume(
        ClaimsPrincipal principal,
        IEscalationOverrideService overrides,
        HttpContext httpContext,
        Guid alertId,
        EscalationOverrideRequest? request,
        CancellationToken cancellationToken)
        => ExecuteAsync(principal, overrides, httpContext, alertId, request, pause: false, cancellationToken);

    private static async Task<IResult> ExecuteAsync(
        ClaimsPrincipal principal,
        IEscalationOverrideService overrides,
        HttpContext httpContext,
        Guid alertId,
        EscalationOverrideRequest? request,
        bool pause,
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
                title: "Invalid escalation command");
        }

        try
        {
            var result = pause
                ? await overrides.PauseAsync(
                    organizationId,
                    userId,
                    CorrelationId(httpContext),
                    new AlertId(alertId),
                    request,
                    httpContext.Request.Headers["Idempotency-Key"].ToString(),
                    cancellationToken)
                : await overrides.ResumeAsync(
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
        catch (EscalationOverrideValidationException exception)
        {
            return exception.IsConflict
                ? Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Escalation conflict", detail: exception.Code)
                : Results.ValidationProblem(
                    new Dictionary<string, string[]> { [exception.Code] = [exception.Message] },
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid escalation command");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Escalation conflict",
                detail: "escalation-conflict");
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
