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

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessingState = OutboxProcessingState.Processed;
        ProcessedAtUtc = UtcInstant.Require(processedAtUtc, nameof(processedAtUtc));
    }
}
