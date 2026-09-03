using System.Text.Json;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Api.Health;
using CriticalAlerts.Api.Http;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

if (args is ["database", "migrate"] or ["database", "reset-demo"])
{
    await DatabaseCommandHost.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var developmentAuthenticationEnabled = builder.Configuration.GetValue("DevelopmentAuthentication:Enabled", false);
var simulationResponsesEnabled = builder.Configuration.GetValue("SimulationResponses:Enabled", false);
SimulationResponseEnvironmentGuard.EnsureAllowed(builder.Environment.EnvironmentName, simulationResponsesEnabled);
builder.Services.AddDevelopmentAuthentication(builder.Environment.EnvironmentName, developmentAuthenticationEnabled);
builder.Services.AddCriticalAlertsPersistence(
    builder.Configuration.GetConnectionString("CriticalAlerts"),
    builder.Configuration["DataProtection:Key"] ?? builder.Configuration["CRITICAL_ALERTS_DATA_PROTECTION_KEY"]);
builder.Services.AddSimulationDispatch();
builder.Services.AddProblemDetails();
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

app.UseAuthentication();
app.UseAuthorization();

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

app.MapDevelopmentAuthenticationEndpoints(developmentAuthenticationEnabled);
app.MapSimulationDispatchEndpoints(builder.Environment.EnvironmentName);
app.MapDeliveryStatusEndpoints();
app.MapRecipientResponseEndpoints(builder.Environment.EnvironmentName, simulationResponsesEnabled);
app.MapAlertLiveEndpoints(builder.Environment.EnvironmentName, simulationResponsesEnabled);
app.MapDirectoryEndpoints();
app.MapAlertDraftEndpoints();

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

/// <summary>Exposes the API host to integration tests.</summary>
public partial class Program
{
}
