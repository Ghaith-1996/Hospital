using System.Security.Claims;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Api.Http;

internal static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this WebApplication app)
    {
        var directory = app.MapGroup("/api/directory");
        directory.MapGet("/practitioners", Search).RequireAuthorization(AuthorizationPolicies.DirectoryReader);
        directory.MapPost("/imports/preview", Preview)
            .RequireAuthorization(AuthorizationPolicies.DirectoryAdministrator)
            .DisableAntiforgery();
        directory.MapPost("/imports", Apply)
            .RequireAuthorization(AuthorizationPolicies.DirectoryAdministrator)
            .DisableAntiforgery();
    }

    private static async Task<IResult> Search(
        ClaimsPrincipal principal,
        IDirectorySearchService search,
        string? q,
        bool includeInactive = true,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetActor(principal, out _, out var organizationId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");
        }

        var results = await search.SearchAsync(new DirectorySearchQuery(organizationId, q, includeInactive), cancellationToken);
        return Results.Ok(results);
    }

    private static Task<IResult> Preview(
        HttpContext httpContext,
        IDirectoryImportService imports,
        CsvDirectorySourceAdapter adapter,
        CancellationToken cancellationToken)
        => RunImportAsync(httpContext, imports, adapter, apply: false, cancellationToken);

    private static Task<IResult> Apply(
        HttpContext httpContext,
        IDirectoryImportService imports,
        CsvDirectorySourceAdapter adapter,
        CancellationToken cancellationToken)
        => RunImportAsync(httpContext, imports, adapter, apply: true, cancellationToken);

    private static async Task<IResult> RunImportAsync(
        HttpContext httpContext,
        IDirectoryImportService imports,
        CsvDirectorySourceAdapter adapter,
        bool apply,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(httpContext.User, out var userId, out var organizationId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized", detail: "authentication-required");
        }

        if (!httpContext.Request.HasFormContentType)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "CSV file required", detail: "csv-file-required");
        }

        var form = await httpContext.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "CSV file required", detail: "csv-file-required");
        }

        var correlationId = httpContext.Response.Headers["X-Correlation-ID"].ToString();
        await using var stream = file.OpenReadStream();
        if (apply)
        {
            var result = await imports.ApplyAsync(organizationId, userId, correlationId, stream, adapter, cancellationToken);
            return result.Applied
                ? Results.Ok(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        }

        var preview = await imports.PreviewAsync(organizationId, userId, correlationId, stream, adapter, cancellationToken);
        return Results.Ok(preview);
    }

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
