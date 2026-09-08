using System.Text.Json;

namespace CriticalAlerts.Domain.Escalation;

// Operational intent contains exactly these identifiers; scope comes from the outbox row.
public sealed class EscalationDispatchRequested
{
    public EscalationDispatchRequested(AlertId alertId, AlertDraftVersion alertVersion, EscalationRunId escalationRunId,
        int stepSequence, IReadOnlyList<AlertRecipientSelectionId> recipientSelectionIds)
    {
        if (alertId.Value == Guid.Empty || escalationRunId.Value == Guid.Empty || alertVersion.Value < 1 || stepSequence < 1
            || recipientSelectionIds.Count == 0 || recipientSelectionIds.Any(x => x.Value == Guid.Empty)
            || recipientSelectionIds.Distinct().Count() != recipientSelectionIds.Count)
            throw new DomainException("Escalation dispatch requires exact unique identifiers.");
        AlertId = alertId;
        AlertVersion = alertVersion;
        EscalationRunId = escalationRunId;
        StepSequence = stepSequence;
        RecipientSelectionIds = recipientSelectionIds.ToArray();
    }
    public AlertId AlertId { get; }
    public AlertDraftVersion AlertVersion { get; }
    public EscalationRunId EscalationRunId { get; }
    public int StepSequence { get; }
    public IReadOnlyList<AlertRecipientSelectionId> RecipientSelectionIds { get; }
    public string ToPayloadJson() => JsonSerializer.Serialize(new
    {
        alertId = AlertId.Value,
        alertVersion = AlertVersion.Value,
        escalationRunId = EscalationRunId.Value,
        stepSequence = StepSequence,
        recipientSelectionIds = RecipientSelectionIds.Select(x => x.Value).Order().ToArray(),
    });
    public string IdempotencyKey => $"escalation-dispatch:{EscalationRunId.Value:N}:step{StepSequence}";
}
