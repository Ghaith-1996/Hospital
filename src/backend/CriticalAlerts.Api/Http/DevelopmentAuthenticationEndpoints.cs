using System.Security.Claims;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CriticalAlerts.Api.Http;

internal static class DevelopmentAuthenticationEndpoints
{
    public static void MapDevelopmentAuthenticationEndpoints(this WebApplication app, bool enabled)
    {
        var api = app.MapGroup(ApiRouteConstants.BasePath)
            .RequireRateLimiting("api");
        api.MapGet("/me", GetCurrentUser).RequireAuthorization().Produces<CurrentUserResponse>().WithApiErrors();
        api.MapGet("/authorization/operator", () => Results.NoContent()).RequireAuthorization(AuthorizationPolicies.Operator).Produces(204).WithApiErrors();
        api.MapGet("/authorization/administrator", () => Results.NoContent()).RequireAuthorization(AuthorizationPolicies.Administrator).Produces(204).WithApiErrors();
        api.MapGet("/authorization/practitioner", () => Results.NoContent()).RequireAuthorization(AuthorizationPolicies.Practitioner).Produces(204).WithApiErrors();
        api.MapGet("/authorization/organization-scope/{organizationId:guid}", CheckOrganizationScope).RequireAuthorization().Produces(204).WithApiErrors();

        if (!enabled)
        {
            return;
        }

        api.MapGet("/dev/identities", async (IDevelopmentIdentityDirectory directory, CancellationToken cancellationToken) =>
        {
            var identities = await directory.ListActiveAsync(cancellationToken);
            return Results.Ok(identities.Select(identity => new DevelopmentIdentityResponse(
                identity.DisplayName,
                identity.SimulationHandle,
                identity.Roles,
                identity.OrganizationId.ToString("D"))));
        }).Produces<IReadOnlyList<DevelopmentIdentityResponse>>().Produces(429);
        api.MapPost("/dev/session", async (DevelopmentSessionRequest? request, IDevelopmentIdentityDirectory directory, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            var identity = await directory.FindActiveByHandleAsync(request?.SimulationHandle ?? string.Empty, cancellationToken);
            if (identity is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown simulation identity", detail: "simulation-handle-not-found");
            }

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, DevelopmentUserPrincipalFactory.Create(identity));
            return Results.NoContent();
        }).Produces(204).ProducesProblem(400).Produces(413).Produces(429);
        api.MapPost("/dev/session/clear", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).Produces(204).Produces(429);
    }

    private static IResult GetCurrentUser(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var organizationId = principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId);
        if (userId is null || organizationId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");
        }

        return Results.Ok(new CurrentUserResponse(
            userId,
            organizationId,
            principal.Identity?.Name ?? string.Empty,
            principal.FindFirstValue(AuthenticationClaimTypes.SimulationHandle) ?? string.Empty,
            principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value).OrderBy(role => role).ToArray(),
            principal.FindFirstValue(AuthenticationClaimTypes.AuthenticationMode) == AuthenticationClaimTypes.DevelopmentMode));
    }

    private static IResult CheckOrganizationScope(Guid organizationId, ClaimsPrincipal principal)
    {
        var claimed = principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId);
        if (!Guid.TryParse(claimed, out var claimedOrganizationId) || claimedOrganizationId != organizationId)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "organization-scope-mismatch");
        }

        return Results.NoContent();
    }
}

internal sealed record DevelopmentSessionRequest(string? SimulationHandle);

internal sealed record CurrentUserResponse(
    string UserId,
    string OrganizationId,
    string DisplayName,
    string SimulationHandle,
    IReadOnlyList<string> Roles,
    bool DevelopmentAuthentication);

internal sealed record DevelopmentIdentityResponse(
    string DisplayName,
    string SimulationHandle,
    IReadOnlyList<string> Roles,
    string OrganizationId);
