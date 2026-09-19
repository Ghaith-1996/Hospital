using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class SimulationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchWorkerOptions> options,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly string leaseOwner = $"simulation-worker-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerOptions = options.Value;
        workerOptions.Validate();
        var logger = loggerFactory.CreateLogger(CriticalAlertsOperationalLog.Category);
        CriticalAlertsOperationalLog.WorkerState(logger, "dispatch", false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            for (var index = 0; index < workerOptions.BatchSize; index++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                using var scope = scopeFactory.CreateScope();
                var escalation = scope.ServiceProvider.GetService<EscalationProcessor>();
                var escalated = false;
                if (escalation is not null)
                {
                    try { escalated = await escalation.ProcessNextAsync(leaseOwner, stoppingToken); }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        scope.ServiceProvider.GetRequiredService<CriticalAlerts.Infrastructure.Persistence.CriticalAlertsDbContext>().ChangeTracker.Clear();
                        CriticalAlertsOperationalLog.WorkerState(logger, "escalation", true);
                    }
                }
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxDispatchProcessor>();
                DispatchProcessingResult result;
                try { result = await processor.ProcessNextAsync(leaseOwner, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    CriticalAlertsOperationalLog.WorkerState(logger, "dispatch", true);
                    break;
                }
                if (!escalated && !result.Processed && !result.Rescheduled && !result.PermanentlyFailed)
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
