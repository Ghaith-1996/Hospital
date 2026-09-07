using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Escalation;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var developmentAuthenticationEnabled = builder.Configuration.GetValue("DevelopmentAuthentication:Enabled", false);
DevelopmentAuthenticationGuard.EnsureAllowed(builder.Environment.EnvironmentName, developmentAuthenticationEnabled);
var simulationDispatchEnabled = builder.Configuration.GetValue("SimulationDispatch:Enabled", false);
SimulationDispatchEnvironmentGuard.EnsureAllowed(builder.Environment.EnvironmentName, simulationDispatchEnabled);
var simulationEscalationEnabled = builder.Configuration.GetValue("SimulationEscalation:Enabled", false);
SimulationEscalationEnvironmentGuard.EnsureAllowed(builder.Environment.EnvironmentName, simulationEscalationEnabled);

if (simulationDispatchEnabled || simulationEscalationEnabled)
{
    var connectionString = builder.Configuration.GetConnectionString("CriticalAlerts");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Simulation workers require a PostgreSQL connection string.");
    }

    builder.Services.AddCriticalAlertsPersistence(
        connectionString,
        builder.Configuration["DataProtection:Key"] ?? builder.Configuration["CRITICAL_ALERTS_DATA_PROTECTION_KEY"]);
}

if (simulationDispatchEnabled)
{
    builder.Services
        .AddOptions<DispatchWorkerOptions>()
        .Bind(builder.Configuration.GetSection("SimulationDispatch"))
        .PostConfigure(options => options.Enabled = true);
    builder.Services.AddSimulationDispatch();
    builder.Services.AddHostedService<SimulationDispatchWorker>();
}
if (simulationEscalationEnabled)
{
    builder.Services.AddOptions<EscalationWorkerOptions>()
        .Bind(builder.Configuration.GetSection("SimulationEscalation"))
        .PostConfigure(options => { options.Enabled = true; options.Validate(); })
        .ValidateOnStart();
    builder.Services.AddSimulationEscalation();
    builder.Services.AddHostedService<SimulationEscalationWorker>();
}
if (!simulationDispatchEnabled && !simulationEscalationEnabled)
{
    builder.Services.AddHostedService<PlatformWorker>();
}

using var host = builder.Build();
await host.RunAsync();

internal sealed class PlatformWorker(ILogger<PlatformWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker platform shell started; business handlers are not enabled.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
