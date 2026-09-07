using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Domain.Escalation;

public sealed class EscalationConsumedSignal
{
    private EscalationConsumedSignal() { }
    public EscalationConsumedSignalId Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public EscalationRunId RunId { get; private set; }
    public AlertId AlertId { get; private set; }
    public AlertDraftVersion AlertVersion { get; private set; }
    public RecipientResponseId ResponseId { get; private set; }
    public AlertRecipientSelectionId RecipientSelectionId { get; private set; }
    public int StepSequence { get; private set; }
    public DateTimeOffset ConsumedAtUtc { get; private set; }

    internal static EscalationConsumedSignal Create(EscalationRun run, RecipientResponseId responseId, AlertRecipientSelectionId selectionId, DateTimeOffset now)
        => new()
        {
            Id = EscalationConsumedSignalId.New(),
            OrganizationId = run.OrganizationId,
            RunId = run.Id,
            AlertId = run.AlertId,
            AlertVersion = run.AlertVersion!.Value,
            ResponseId = responseId,
            RecipientSelectionId = selectionId,
            StepSequence = run.CurrentStep,
            ConsumedAtUtc = UtcInstant.Require(now, nameof(now)),
        };
}
