using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Domain.Escalation;

public enum EscalationOverrideReason { OperatorReview, ManualCoordination, ReadyToResume }

// Identifier-only timeline evidence. No arbitrary metadata, message or reason text.
public sealed class EscalationEvent
{
    private EscalationEvent() { }
    public EscalationEventId Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public AlertDraftVersion AlertVersion { get; private set; }
    public EscalationRunId RunId { get; private set; }
    public EscalationEventType EventType { get; private set; }
    public int StepSequence { get; private set; }
    public UserId? ActorUserId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public AlertRecipientSelectionId? RecipientSelectionId { get; private set; }
    public EscalationFailureCategory? FailureCategory { get; private set; }
    public EscalationOverrideReason? OverrideReason { get; private set; }

    public static EscalationEvent Record(EscalationRun run, EscalationEventType type, int step, UserId? actor, Guid correlationId, DateTimeOffset now,
        AlertRecipientSelectionId? recipientSelectionId = null, EscalationFailureCategory? failureCategory = null,
        EscalationOverrideReason? overrideReason = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        UtcInstant.Require(now, nameof(now));
        if (run.AlertVersion is null || run.PlanId is null || !Enum.IsDefined(type) || step < 1 || step > run.CurrentStep
            || correlationId == Guid.Empty || actor?.Value == Guid.Empty || recipientSelectionId?.Value == Guid.Empty
            || now < run.StartedAtUtc || (type is EscalationEventType.Paused or EscalationEventType.Resumed && actor is null)
            || (type == EscalationEventType.RecipientActivated) != (recipientSelectionId is not null)
            || (type == EscalationEventType.ProcessingFailed) != (failureCategory is not null)
            || (failureCategory is { } category && !Enum.IsDefined(category))
            || (type is EscalationEventType.Paused or EscalationEventType.Resumed) != (overrideReason is not null)
            || (type == EscalationEventType.Paused && overrideReason is not (EscalationOverrideReason.OperatorReview or EscalationOverrideReason.ManualCoordination))
            || (type == EscalationEventType.Resumed && overrideReason != EscalationOverrideReason.ReadyToResume)
            || (overrideReason is { } reason && !Enum.IsDefined(reason)))
            throw new DomainException("Escalation events require exact identifiers and allowlisted evidence.");
        return new EscalationEvent
        {
            Id = EscalationEventId.New(),
            OrganizationId = run.OrganizationId,
            AlertId = run.AlertId,
            AlertVersion = run.AlertVersion.Value,
            RunId = run.Id,
            EventType = type,
            StepSequence = step,
            ActorUserId = actor,
            CorrelationId = correlationId,
            OccurredAtUtc = now,
            RecipientSelectionId = recipientSelectionId,
            FailureCategory = failureCategory,
            OverrideReason = overrideReason,
        };
    }
}
