namespace CriticalAlerts.Domain.Reliability;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        EventType = string.Empty;
        PayloadJson = string.Empty;
        IdempotencyKey = string.Empty;
        LastErrorCategory = string.Empty;
    }

    private OutboxMessage(
        OutboxMessageId id,
        OrganizationId organizationId,
        string eventType,
        Guid aggregateId,
        string payloadJson,
        string idempotencyKey,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        EventType = eventType;
        AggregateId = aggregateId;
        PayloadJson = payloadJson;
        IdempotencyKey = idempotencyKey;
        ProcessingState = OutboxProcessingState.Pending;
        AttemptCount = 0;
        NextAttemptAtUtc = createdAtUtc;
        CreatedAtUtc = createdAtUtc;
        LastErrorCategory = string.Empty;
    }

    public OutboxMessageId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string EventType { get; private set; }

    public Guid AggregateId { get; private set; }

    public string PayloadJson { get; private set; }

    public string IdempotencyKey { get; private set; }

    public OutboxProcessingState ProcessingState { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public string LastErrorCategory { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }

    public static OutboxMessage Create(
        OutboxMessageId id,
        OrganizationId organizationId,
        string eventType,
        Guid aggregateId,
        string payloadJson,
        string idempotencyKey,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(payloadJson) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new DomainException("Outbox messages require an event type, identifier payload, and idempotency key.");
        }

        if (payloadJson.Contains("SIMULATION:", StringComparison.OrdinalIgnoreCase) is false
            && (payloadJson.Contains("patient", StringComparison.OrdinalIgnoreCase)
                || payloadJson.Contains("beats/min", StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("Outbox payloads must contain identifiers only.");
        }

        return new OutboxMessage(
            id,
            organizationId,
            eventType.Trim(),
            aggregateId,
            payloadJson.Trim(),
            idempotencyKey.Trim(),
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }

    public bool TryAcquireLease(string leaseOwner, DateTimeOffset nowUtc, DateTimeOffset leaseExpiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new DomainException("Outbox leases require a lease owner.");
        }

        var now = UtcInstant.Require(nowUtc, nameof(nowUtc));
        var expires = UtcInstant.Require(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (expires <= now)
        {
            throw new DomainException("Outbox leases must expire after their acquisition time.");
        }

        if (ProcessingState is OutboxProcessingState.Processed or OutboxProcessingState.Failed)
        {
            return false;
        }

        if (ProcessingState == OutboxProcessingState.Pending && NextAttemptAtUtc > now)
        {
            return false;
        }

        if (ProcessingState == OutboxProcessingState.Processing
            && LeaseExpiresAtUtc is DateTimeOffset currentExpiry
            && currentExpiry > now
            && !string.Equals(LeaseOwner, leaseOwner.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        ProcessingState = OutboxProcessingState.Processing;
        LeaseOwner = leaseOwner.Trim();
        LeaseExpiresAtUtc = expires;
        AttemptCount++;
        return true;
    }

    public void MarkProcessed(string leaseOwner, DateTimeOffset processedAtUtc)
    {
        EnsureLeaseOwner(leaseOwner);
        ProcessingState = OutboxProcessingState.Processed;
        ProcessedAtUtc = UtcInstant.Require(processedAtUtc, nameof(processedAtUtc));
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    public void ScheduleRetry(
        string leaseOwner,
        DateTimeOffset nowUtc,
        DateTimeOffset nextAttemptAtUtc,
        string errorCategory)
    {
        EnsureLeaseOwner(leaseOwner);
        var now = UtcInstant.Require(nowUtc, nameof(nowUtc));
        var nextAttempt = UtcInstant.Require(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (nextAttempt < now)
        {
            throw new DomainException("Outbox retry time cannot be before the current time.");
        }

        if (string.IsNullOrWhiteSpace(errorCategory))
        {
            throw new DomainException("Outbox retries require a safe error category.");
        }

        ProcessingState = OutboxProcessingState.Pending;
        NextAttemptAtUtc = nextAttempt;
        LastErrorCategory = errorCategory.Trim();
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    public void MarkFailed(string leaseOwner, DateTimeOffset failedAtUtc, string errorCategory)
    {
        EnsureLeaseOwner(leaseOwner);
        if (string.IsNullOrWhiteSpace(errorCategory))
        {
            throw new DomainException("Outbox failures require a safe error category.");
        }

        ProcessingState = OutboxProcessingState.Failed;
        LastErrorCategory = errorCategory.Trim();
        ProcessedAtUtc = UtcInstant.Require(failedAtUtc, nameof(failedAtUtc));
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    // Retained for existing domain callers that mark an outbox row directly before worker leasing exists.
    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        if (ProcessingState == OutboxProcessingState.Processing)
        {
            throw new DomainException("A leased outbox message must be completed by its lease owner.");
        }

        ProcessingState = OutboxProcessingState.Processed;
        ProcessedAtUtc = UtcInstant.Require(processedAtUtc, nameof(processedAtUtc));
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void EnsureLeaseOwner(string leaseOwner)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner)
            || ProcessingState != OutboxProcessingState.Processing
            || !string.Equals(LeaseOwner, leaseOwner.Trim(), StringComparison.Ordinal))
        {
            throw new DomainException("The outbox operation requires the current lease owner.");
        }
    }
}
