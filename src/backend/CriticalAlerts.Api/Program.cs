using System.Text.Json;
using CriticalAlerts.Api.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Keep local platform logs console-only so expected dependency failures cannot trigger a Windows EventLog write.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();

app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-ID";
    var supplied = context.Request.Headers[headerName].ToString();
    var correlationId = IsSafeCorrelationId(supplied) ? supplied : Guid.NewGuid().ToString("N");

    context.Response.Headers[headerName] = correlationId;
    await next(context);
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteSafeHealthResponse,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal),
    ResponseWriter = WriteSafeHealthResponse,
});

await app.RunAsync();

static bool IsSafeCorrelationId(string value)
{
    return value.Length is > 0 and <= 96 && value.All(character =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

static Task WriteSafeHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    context.Response.StatusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

    var checks = report.Entries.ToDictionary(
        entry => entry.Key,
        entry => new
        {
            status = entry.Value.Status.ToString(),
            description = entry.Value.Status == HealthStatus.Healthy ? "available" : "unavailable",
        });

    var payload = new
    {
        status = report.Status.ToString(),
        correlationId = context.Response.Headers["X-Correlation-ID"].ToString(),
        checks,
    };

    return JsonSerializer.SerializeAsync(context.Response.Body, payload);
}

/// <summary>Exposes the Phase 1 API host to integration tests.</summary>
public partial class Program
{
}
