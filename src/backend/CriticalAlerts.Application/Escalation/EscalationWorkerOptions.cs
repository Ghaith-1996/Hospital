namespace CriticalAlerts.Application.Escalation;

public sealed class EscalationWorkerOptions
{
    public bool Enabled { get; set; }
    public int BatchSize { get; set; } = 10;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (BatchSize is < 1 or > 100 || PollIntervalMilliseconds is < 1 or > 300000
            || LeaseDuration <= TimeSpan.Zero || LeaseDuration > TimeSpan.FromHours(1))
            throw new InvalidOperationException("Simulation escalation requires bounded batch, polling and lease settings.");
    }
}
