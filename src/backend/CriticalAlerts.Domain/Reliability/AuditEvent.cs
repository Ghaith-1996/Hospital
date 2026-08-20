namespace CriticalAlerts.Domain.Reliability;

public sealed class AuditEvent
{
    private AuditEvent()
    {
        ActorType = string.Empty;
        Action = string.Empty;
        ResourceType = string.Empty;
        Outcome = string.Empty;
        CorrelationId = string.Empty;
        SanitizedMetadata = string.Empty;
    }

    private AuditEvent(
        AuditEventId id,
        OrganizationId organizationId,
        string actorType,
        UserId? actorUserId,
        string action,
        string resourceType,
        Guid resourceId,
        string outcome,
        string correlationId,
        string sanitizedMetadata,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        ActorType = actorType;
        ActorUserId = actorUserId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        Outcome = outcome;
        CorrelationId = correlationId;
        SanitizedMetadata = sanitizedMetadata;
        OccurredAtUtc = occurredAtUtc;
    }

    public AuditEventId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string ActorType { get; private set; }

    public UserId? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public string ResourceType { get; private set; }

    public Guid ResourceId { get; private set; }

    public string Outcome { get; private set; }

    public string CorrelationId { get; private set; }

    public string SanitizedMetadata { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static AuditEvent Record(
        AuditEventId id,
        OrganizationId organizationId,
        string actorType,
        UserId? actorUserId,
        string action,
        string resourceType,
        Guid resourceId,
        string outcome,
        string correlationId,
        string sanitizedMetadata,
        DateTimeOffset occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(correlationId))
        {
            throw new DomainException("Audit events require action, resource type, and correlation ID.");
        }

        return new AuditEvent(
            id,
            organizationId,
            actorType.Trim(),
            actorUserId,
            action.Trim(),
            resourceType.Trim(),
            resourceId,
            outcome.Trim(),
            correlationId.Trim(),
            sanitizedMetadata.Trim(),
            UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc)));
    }
}
