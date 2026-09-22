namespace CriticalAlerts.Domain.Delivery;

public sealed class EscalationEvent
{
    private EscalationEvent() { }
    public Guid Id { get; private set; }
    public EscalationRunId RunId { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public int Sequence { get; private set; }
    public EscalationEventKind Kind { get; private set; }
    public int Step { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public Guid? RecipientSelectionId { get; private set; }
    public UserId? ActorUserId { get; private set; }

    internal static EscalationEvent Record(EscalationRunId runId, OrganizationId organizationId, int sequence,
        EscalationEventKind kind, int step, DateTimeOffset now, Guid? recipientSelectionId, UserId? actor)
    {
        if (!Enum.IsDefined(kind)) throw new DomainException("An allowed escalation event is required.");
        return new EscalationEvent
        {
            Id = Guid.NewGuid(), RunId = runId, OrganizationId = organizationId, Sequence = sequence,
            Kind = kind, Step = step, OccurredAtUtc = UtcInstant.Require(now, nameof(now)),
            RecipientSelectionId = recipientSelectionId, ActorUserId = actor,
        };
    }
}
