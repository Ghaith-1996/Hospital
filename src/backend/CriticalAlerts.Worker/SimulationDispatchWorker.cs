using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Dispatch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class SimulationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchWorkerOptions> options,
    ILogger<SimulationDispatchWorker> logger) : BackgroundService
{
    private readonly string leaseOwner = $"simulation-worker-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        workerOptions.Validate();
        logger.LogInformation("Simulation dispatch worker started with lease owner {LeaseOwner}.", leaseOwner);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            for (var index = 0; index < workerOptions.BatchSize; index++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxDispatchProcessor>();
                var result = await processor.ProcessNextAsync(leaseOwner, stoppingToken);
                if (!result.Processed && !result.Rescheduled && !result.PermanentlyFailed)
                {
                    break;
                }

                processed++;
            }

            if (processed == 0)
            {
                await Task.Delay(workerOptions.PollIntervalMilliseconds, stoppingToken);
            }
        }
    }
}
