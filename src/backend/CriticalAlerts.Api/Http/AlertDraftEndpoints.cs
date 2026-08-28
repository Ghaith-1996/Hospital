using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Api.Http;

internal static class AlertDraftEndpoints
{
    public static void MapAlertDraftEndpoints(this WebApplication app)
    {
        var alerts = app.MapGroup("/api/alerts").RequireAuthorization(AuthorizationPolicies.AlertDraftEditor);
        alerts.MapPost("/drafts", Create);
        alerts.MapGet("/{alertId:guid}/review", Review);
        alerts.MapGet("/{alertId:guid}", Get);
        alerts.MapPatch("/{alertId:guid}", Update);
        alerts.MapPost("/{alertId:guid}/field-confirmations", ConfirmCriticalField);
        alerts.MapPost("/{alertId:guid}/submit-for-confirmation", Submit);
        alerts.MapPut("/{alertId:guid}/approved-message", SetApprovedMessage);
        alerts.MapPut("/{alertId:guid}/recipients", ReplaceRecipients);
    }

    private static async Task<IResult> Create(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        CreateAlertDraftRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["An alert draft request is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid alert draft");
        }

        try
        {
            var draft = await drafts.CreateAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                request,
                cancellationToken);
            return Results.Created($"/api/alerts/{draft.AlertId:D}", draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DomainException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> Get(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out _, out var organizationId))
        {
            return Unauthorized();
        }

        var draft = await drafts.GetAsync(organizationId, new AlertId(alertId), cancellationToken);
        return draft is null ? NotFound() : Results.Ok(draft);
    }

    private static async Task<IResult> Review(
        ClaimsPrincipal principal,
        IAlertReviewService reviews,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out _, out var organizationId))
        {
            return Unauthorized();
        }

        try
        {
            var review = await reviews.GetAsync(organizationId, new AlertId(alertId), cancellationToken);
            return review is null ? NotFound() : Results.Ok(review);
        }
        catch (Exception exception) when (exception is AlertReviewValidationException or DomainException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> Update(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        UpdateAlertDraftRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["An alert draft update is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid alert draft");
        }

        try
        {
            var draft = await drafts.UpdateAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                cancellationToken);
            return draft is null ? NotFound() : Results.Ok(draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DomainException or DbUpdateConcurrencyException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ConfirmCriticalField(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        ConfirmAlertCriticalFieldRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A critical-field confirmation request is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid critical-field confirmation");
        }

        try
        {
            var draft = await drafts.ConfirmCriticalFieldAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                cancellationToken);
            return draft is null ? NotFound() : Results.Ok(draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DomainException or DbUpdateConcurrencyException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> Submit(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        SubmitAlertDraftRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A submit request is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid alert draft");
        }

        try
        {
            var draft = await drafts.SubmitAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                cancellationToken);
            return draft is null ? NotFound() : Results.Ok(draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DomainException or DbUpdateConcurrencyException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> SetApprovedMessage(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        SetApprovedMessageRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["An approved-message request is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid approved message");
        }

        try
        {
            var draft = await drafts.SetApprovedMessageAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                cancellationToken);
            return draft is null ? NotFound() : Results.Ok(draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DomainException or DbUpdateConcurrencyException)
        {
            return Rejected(exception);
        }
    }

    private static async Task<IResult> ReplaceRecipients(
        ClaimsPrincipal principal,
        IAlertDraftService drafts,
        Guid alertId,
        ReplaceAlertRecipientsRequest? request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(principal, out var userId, out var organizationId))
        {
            return Unauthorized();
        }

        if (request is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["A complete recipient replacement request is required."],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid recipient selection");
        }

        try
        {
            var draft = await drafts.ReplaceRecipientsAsync(
                organizationId,
                userId,
                CorrelationId(httpContext),
                new AlertId(alertId),
                request,
                cancellationToken);
            return draft is null ? NotFound() : Results.Ok(draft);
        }
        catch (Exception exception) when (exception is AlertDraftValidationException or DirectorySelectionValidationException or DomainException or DbUpdateConcurrencyException)
        {
            return Rejected(exception);
        }
    }

    private static IResult Rejected(Exception exception)
    {
        if (exception is StaleAlertVersionException or DirectorySelectionRevisionConflictException or DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Draft version is stale",
                detail: "draft-version-stale");
        }

        if (exception is AlertReviewValidationException reviewValidation)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Alert review is not current",
                detail: reviewValidation.Code);
        }

        if (exception is AlertDraftValidationException validation)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [validation.Code] = [validation.Message],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid alert draft");
        }

        if (exception is DirectorySelectionValidationException directoryValidation)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [directoryValidation.Code] = [directoryValidation.Message],
            }, statusCode: StatusCodes.Status400BadRequest, title: "Invalid recipient selection");
        }

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Alert draft rejected",
            detail: "alert-draft-rejected");
    }

    private static IResult Unauthorized()
        => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");

    private static IResult NotFound()
        => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "alert-not-found");

    private static string CorrelationId(HttpContext httpContext)
        => httpContext.Response.Headers["X-Correlation-ID"].ToString();

    private static bool TryGetActor(ClaimsPrincipal principal, out UserId userId, out OrganizationId organizationId)
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
}
