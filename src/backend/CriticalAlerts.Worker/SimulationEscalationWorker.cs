using CriticalAlerts.Application.Escalation;
using CriticalAlerts.Infrastructure.Escalation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class SimulationEscalationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<EscalationWorkerOptions> options,
    ILogger<SimulationEscalationWorker> logger) : BackgroundService
{
    // Stable per process; the repository adds a unique token for each acquisition.
    private readonly string processOwner = $"escalation-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = options.Value;
        configuration.Validate();
        logger.LogInformation("DEMO escalation scheduler started with process owner {ProcessOwner}.", processOwner);
        while (!stoppingToken.IsCancellationRequested)
        {
            for (var index = 0; index < configuration.BatchSize; index++)
            {
                stoppingToken.ThrowIfCancellationRequested();
                using var scope = scopeFactory.CreateScope();
                // Scheduling only until the atomic activation/outbox processor is integrated.
                if (!await scope.ServiceProvider.GetRequiredService<EscalationScheduler>().ScheduleNextAsync(stoppingToken)) break;
            }
            await Task.Delay(configuration.PollIntervalMilliseconds, stoppingToken);
        }
    }
}
