namespace CriticalAlerts.Domain.Delivery;

/// <summary>
/// Durable first-send time of one provider attempt, committed before the network call so a replay after a
/// rolled-back dispatch transaction presents the same repeatable request. Holds no recipient or message data.
/// </summary>
public sealed class ProviderSendRecord
{
    private ProviderSendRecord()
    {
        Provider = string.Empty;
        AttemptIdempotencyKey = string.Empty;
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Provider { get; private set; }

    public string AttemptIdempotencyKey { get; private set; }

    public DateTimeOffset FirstSentAtUtc { get; private set; }
}
