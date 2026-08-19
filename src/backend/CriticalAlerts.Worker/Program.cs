using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
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
