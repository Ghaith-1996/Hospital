namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class DispatchWorkerOptions
{
    public bool Enabled { get; set; }

    public int PollIntervalMilliseconds { get; set; } = 1_000;

    public int BatchSize { get; set; } = 10;

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public int MaxAttempts { get; set; } = 2;

    public void Validate()
    {
        if (PollIntervalMilliseconds <= 0 || PollIntervalMilliseconds > 300_000)
        {
            throw new InvalidOperationException("Simulation dispatch polling must be between 1 and 300000 milliseconds.");
        }

        if (BatchSize <= 0 || BatchSize > 100)
        {
            throw new InvalidOperationException("Simulation dispatch batch size must be between 1 and 100.");
        }

        if (LeaseDuration <= TimeSpan.Zero || LeaseDuration > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("Simulation dispatch leases must be greater than zero and no longer than one hour.");
        }

        if (RetryDelay <= TimeSpan.Zero || RetryDelay > TimeSpan.FromHours(1))
        {
            throw new InvalidOperationException("Simulation dispatch retries must be greater than zero and no longer than one hour.");
        }

        if (MaxAttempts <= 0 || MaxAttempts > 10)
        {
            throw new InvalidOperationException("Simulation dispatch attempts must be between 1 and 10.");
        }
    }
}
