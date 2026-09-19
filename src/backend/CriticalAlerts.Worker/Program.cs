using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
CriticalAlertsOperationalLog.Configure(builder.Logging);
var developmentAuthenticationEnabled = builder.Configuration.GetValue("DevelopmentAuthentication:Enabled", false);
DevelopmentAuthenticationGuard.EnsureAllowed(builder.Environment.EnvironmentName, developmentAuthenticationEnabled);
var simulationDispatchEnabled = builder.Configuration.GetValue("SimulationDispatch:Enabled", false);
SimulationDispatchEnvironmentGuard.EnsureAllowed(builder.Environment.EnvironmentName, simulationDispatchEnabled);
var simulationEscalationEnabled = builder.Configuration.GetValue("SimulationEscalation:Enabled", false);
SimulationDispatchEnvironmentGuard.EnsureAllowed(builder.Environment.EnvironmentName, simulationEscalationEnabled);
if (simulationEscalationEnabled && !simulationDispatchEnabled)
    throw new InvalidOperationException("Simulation escalation requires simulation dispatch.");

if (simulationDispatchEnabled)
{
    var connectionString = builder.Configuration.GetConnectionString("CriticalAlerts");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Simulation dispatch requires a PostgreSQL connection string.");
    }

    builder.Services.AddCriticalAlertsPersistence(
        connectionString,
        builder.Configuration["DataProtection:Key"] ?? builder.Configuration["CRITICAL_ALERTS_DATA_PROTECTION_KEY"]);
    builder.Services
        .AddOptions<DispatchWorkerOptions>()
        .Bind(builder.Configuration.GetSection("SimulationDispatch"))
        .PostConfigure(options => options.Enabled = true);
    builder.Services.AddSimulationDispatch();
    if (simulationEscalationEnabled) builder.Services.AddScoped<EscalationProcessor>();
    builder.Services.AddHostedService<SimulationDispatchWorker>();
}
else
{
    builder.Services.AddHostedService<PlatformWorker>();
}

using var host = builder.Build();
await host.RunAsync();

internal sealed class PlatformWorker(ILoggerFactory loggerFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CriticalAlertsOperationalLog.WorkerState(
            loggerFactory.CreateLogger(CriticalAlertsOperationalLog.Category), "platform", false);
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
