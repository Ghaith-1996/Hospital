using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CriticalAlerts.Application.Identity;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var developmentAuthenticationEnabled = builder.Configuration.GetValue("DevelopmentAuthentication:Enabled", false);
DevelopmentAuthenticationGuard.EnsureAllowed(builder.Environment.EnvironmentName, developmentAuthenticationEnabled);
builder.Services.AddHostedService<PlatformWorker>();

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
