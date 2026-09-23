using System.Globalization;
using System.Security.Claims;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using Microsoft.OpenApi;

namespace CriticalAlerts.Api.Http;

internal static class AuditEndpoints
{
    private static readonly string[] Parameters =
        ["occurredFromUtc", "occurredToUtc", "action", "outcome", "resourceType", "correlationId", "cursor", "pageSize"];

    public static void MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet($"{ApiRouteConstants.BasePath}/admin/audit", Get)
            .RequireAuthorization(AuthorizationPolicies.AuditReader)
            .RequireRateLimiting("api")
            .WithReadOnlyProjection<AuditPage>("Read bounded safe audit events for the authenticated organization. Audit access is itself recorded.", 400, 503)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters = Parameters.Select(name => (IOpenApiParameter)new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = name switch
                    {
                        "pageSize" => "Default 50; minimum 1; maximum 100.",
                        "occurredFromUtc" => "Inclusive UTC timestamp.",
                        "occurredToUtc" => "Exclusive UTC timestamp.",
                        "cursor" => "Opaque server-provided timestamp/event cursor.",
                        _ => "Exact allowlisted technical value.",
                    },
                    Schema = name == "pageSize"
                        ? new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "1", Maximum = "100" }
                        : new OpenApiSchema { Type = JsonSchemaType.String },
                }).ToList();
                return Task.CompletedTask;
            });
    }

    private static async Task<IResult> Get(HttpContext context, IAuditQueryService audit, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(AuthenticationClaimTypes.OrganizationId), out var organization)
            || !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
            return Problem(context, 401, "Unauthorized", "authentication-required");
        try
        {
            var values = context.Request.Query;
            if (values.Any(p => !Parameters.Contains(p.Key, StringComparer.Ordinal) || p.Value.Count != 1))
                throw new AuditQueryValidationException();
            string? Value(string key) => values.TryGetValue(key, out var value) ? value.ToString() : null;
            var sizeText = Value("pageSize");
            var size = 50;
            if (sizeText is not null && !int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out size))
                throw new AuditQueryValidationException();
            var query = new AuditQuery(ParseUtc(Value("occurredFromUtc")), ParseUtc(Value("occurredToUtc")),
                Value("action"), Value("outcome"), Value("resourceType"), Value("correlationId"), Value("cursor"), size);
            var page = await audit.QueryAsync(new OrganizationId(organization), new UserId(actor),
                context.Response.Headers["X-Correlation-ID"].ToString(), query, cancellationToken);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(page);
        }
        catch (AuditQueryValidationException)
        {
            return Problem(context, 400, "Invalid audit query", "Audit query is invalid. Review the filters and pagination.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Problem(context, 503, "Audit unavailable", "Audit data could not be read. Retry when the database is available.");
        }
    }

    private static DateTimeOffset? ParseUtc(string? value)
    {
        if (value is null) return null;
        if (value.Length > 40 || !(value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed.Offset != TimeSpan.Zero)
            throw new AuditQueryValidationException();
        return parsed;
    }

    private static IResult Problem(HttpContext context, int status, string title, string detail)
        => Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["correlationId"] = context.Response.Headers["X-Correlation-ID"].ToString() });
}
