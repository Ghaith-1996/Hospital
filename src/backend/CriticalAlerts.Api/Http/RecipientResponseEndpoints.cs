using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using static CriticalAlerts.Api.Http.EndpointHelpers;

namespace CriticalAlerts.Api.Http;

internal static class RecipientResponseEndpoints
{
    public static void MapRecipientResponseEndpoints(
        this WebApplication app,
        string environmentName,
        bool enabled)
    {
        if (!enabled || !SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environmentName))
        {
            return;
        }

        var group = app.MapGroup($"{ApiRouteConstants.BasePath}/my-alerts")
            .RequireAuthorization(AuthorizationPolicies.PractitionerAlertResponder)
            .RequireRateLimiting("api");
        group.MapGet("/", List).Produces<IReadOnlyList<MyAlertSummaryView>>().WithApiErrors(400);
        group.MapGet("/{alertId:guid}", Get).Produces<MyAlertDetailView>().WithApiErrors(400, 404);
        group.MapPost("/{alertId:guid}/opened", MarkOpened).WithIdempotencyHeader().Produces<OpenedRecipientAlertResult>().WithApiErrors(400, 404, 409).Produces(413);
        group.MapPost("/{alertId:guid}/responses", RecordResponse).WithIdempotencyHeader().Produces<RecipientResponseResult>().WithApiErrors(400, 404, 409).Produces(413);
    }

    private static async Task<IResult> List(
        ClaimsPrincipal principal,
        IRecipientInboxService inbox,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        try
        {
            return Results.Ok(await inbox.ListAsync(organizationId, userId, cancellationToken));
        }
        catch (RecipientResponseValidationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        IRecipientInboxService inbox,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await inbox.GetAsync(
                organizationId,
                userId,
                new AlertId(alertId),
                cancellationToken);
            return result is null ? NotFound() : Results.Ok(result);
        }
        catch (RecipientResponseValidationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> MarkOpened(
        ClaimsPrincipal principal,
        IRecipientResponseService responses,
        HttpContext httpContext,
        Guid alertId,
        OpenRecipientAlertRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Invalid("request-required", "An exact alert version is required.");
        }

        try
        {
            var result = await responses.MarkOpenedAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                httpContext.Request.Headers["Idempotency-Key"].ToString(),
                cancellationToken);
            return result is null ? NotFound() : Results.Ok(result);
        }
        catch (RecipientResponseValidationException exception)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> RecordResponse(
        ClaimsPrincipal principal,
        IRecipientResponseService responses,
        HttpContext httpContext,
        Guid alertId,
        RecordRecipientResponseRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Invalid("request-required", "An exact alert version and response type are required.");
        }

        try
        {
            var result = await responses.RecordAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                httpContext.Request.Headers["Idempotency-Key"].ToString(),
                cancellationToken);
            return result is null ? NotFound() : Results.Ok(result);
        }
        catch (RecipientResponseValidationException exception)
        {
            return Rejected(exception);
        }
    }

    private static IResult Rejected(RecipientResponseValidationException exception)
    {
        if (exception.Code == "practitioner-link-required")
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Practitioner access unavailable",
                detail: exception.Code);
        }

        if (exception.Code is "alert-version-stale"
            or "idempotency-conflict"
            or "response-in-progress"
            or "response-conflict"
            or "terminal-disposition-conflict")
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Recipient response conflict",
                detail: exception.Code);
        }

        return Invalid(exception.Code, exception.Message);
    }

    private static IResult Invalid(string code, string message)
        => Results.ValidationProblem(
            new Dictionary<string, string[]> { [code] = [message] },
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid simulation recipient action");
}
