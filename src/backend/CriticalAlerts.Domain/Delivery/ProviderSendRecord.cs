namespace CriticalAlerts.Domain.Delivery;

/// <summary>
/// Durable first-send time of one provider attempt, committed before the network call so a replay after a
/// rolled-back dispatch transaction presents the same repeatable request. Holds no recipient or message data.
/// Voice sends also bind the opaque callback tag and a one-way fingerprint of the send settings, so callbacks that
/// beat the send commit can be held and a replay under changed settings is refused.
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

    public string? CallbackTag { get; private set; }

    public string? OperationFingerprint { get; private set; }
}
