using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Api.Http;

internal static class DevelopmentLocationContextEndpoints
{
    public static void MapDevelopmentLocationContextEndpoints(this WebApplication app, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        app.MapGet("/api/v1/dev/location-context", GetLocationContextAsync)
            .RequireAuthorization(AuthorizationPolicies.AlertDraftEditor)
            .RequireRateLimiting("api")
            .Produces<SimulationLocationContextResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetLocationContextAsync(
        ClaimsPrincipal principal,
        CriticalAlertsDbContext db,
        CancellationToken cancellationToken)
    {
        var organizationClaim = principal.FindFirstValue(AuthenticationClaimTypes.OrganizationId);
        if (!Guid.TryParse(organizationClaim, out var organizationValue))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "authentication-required");
        }

        var organizationId = new OrganizationId(organizationValue);
        var sites = await db.Sites
            .AsNoTracking()
            .Where(site => site.OrganizationId == organizationId)
            .OrderBy(site => site.Name)
            .ThenBy(site => site.Id)
            .Select(site => new { site.Id, site.Name })
            .ToArrayAsync(cancellationToken);
        var departments = await db.Departments
            .AsNoTracking()
            .Where(department => department.OrganizationId == organizationId)
            .OrderBy(department => department.Name)
            .ThenBy(department => department.Id)
            .Select(department => new { department.Id, department.SiteId, department.Name })
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new SimulationLocationContextResponse(
            organizationId.Value.ToString("D"),
            sites.Select(site => new SimulationSiteResponse(
                site.Id.Value.ToString("D"),
                site.Name,
                departments
                    .Where(department => department.SiteId == site.Id)
                    .Select(department => new SimulationDepartmentResponse(
                        department.Id.Value.ToString("D"),
                        department.Name))
                    .ToArray()))
                .ToArray()));
    }
}

internal sealed record SimulationLocationContextResponse(
    string OrganizationId,
    IReadOnlyList<SimulationSiteResponse> Sites);

internal sealed record SimulationSiteResponse(
    string SiteId,
    string Name,
    IReadOnlyList<SimulationDepartmentResponse> Departments);

internal sealed record SimulationDepartmentResponse(string DepartmentId, string Name);
