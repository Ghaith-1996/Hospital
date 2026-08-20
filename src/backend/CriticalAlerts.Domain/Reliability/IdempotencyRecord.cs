namespace CriticalAlerts.Domain.Reliability;

public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
        OperationType = string.Empty;
        IdempotencyKey = string.Empty;
        RequestHash = string.Empty;
        ResultReference = string.Empty;
    }

    private IdempotencyRecord(
        IdempotencyRecordId id,
        OrganizationId organizationId,
        string operationType,
        string idempotencyKey,
        string requestHash,
        IdempotencyProcessingStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? expiresAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        OperationType = operationType;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        ResultReference = string.Empty;
    }

    public IdempotencyRecordId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string OperationType { get; private set; }

    public string IdempotencyKey { get; private set; }

    public string RequestHash { get; private set; }

    public IdempotencyProcessingStatus Status { get; private set; }

    public string ResultReference { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public static IdempotencyRecord Start(
        IdempotencyRecordId id,
        OrganizationId organizationId,
        string operationType,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(operationType) || string.IsNullOrWhiteSpace(idempotencyKey) || string.IsNullOrWhiteSpace(requestHash))
        {
            throw new DomainException("Idempotency records require an operation, key, and request hash.");
        }

        return new IdempotencyRecord(
            id,
            organizationId,
            operationType.Trim(),
            idempotencyKey.Trim(),
            requestHash.Trim(),
            IdempotencyProcessingStatus.Started,
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)),
            expiresAtUtc: null);
    }

    public void Complete(string resultReference)
    {
        Status = IdempotencyProcessingStatus.Completed;
        ResultReference = resultReference.Trim();
    }
}
