namespace CriticalAlerts.Domain.Escalation;

public sealed class AlertEscalationRecipientSnapshot
{
    private AlertEscalationRecipientSnapshot() { }
    public AlertEscalationRecipientSnapshotId Id { get; private set; }
    public AlertEscalationPlanId PlanId { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public int AlertVersion { get; private set; }
    public EscalationPolicyId EscalationPolicyId { get; private set; }
    public string EscalationPolicyVersion { get; private set; } = string.Empty;
    public string PlanRevision { get; private set; } = string.Empty;
    public int StepSequence { get; private set; }
    public PractitionerId PractitionerId { get; private set; }
    public PractitionerRoleId PractitionerRoleId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string DirectoryRevision { get; private set; } = string.Empty;
    public DateTimeOffset? DirectorySourceUpdatedAtUtc { get; private set; }
    public string OnCallSnapshot { get; private set; } = string.Empty;
    public UserId ConfirmedByUserId { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    public static IReadOnlyList<AlertEscalationRecipientSnapshot> FromPlan(AlertEscalationPlan plan)
        => plan.Definition.Steps.SelectMany(step => step.Recipients.Select(recipient => new AlertEscalationRecipientSnapshot
        {
            Id = AlertEscalationRecipientSnapshotId.New(), PlanId = plan.Id, OrganizationId = plan.OrganizationId,
            AlertId = plan.AlertId, AlertVersion = plan.AlertVersion, EscalationPolicyId = plan.EscalationPolicyId,
            EscalationPolicyVersion = plan.EscalationPolicyVersion, PlanRevision = plan.Revision,
            StepSequence = step.Sequence, PractitionerId = new(recipient.PractitionerId), PractitionerRoleId = new(recipient.PractitionerRoleId),
            Channel = Enum.Parse<NotificationChannel>(recipient.Channel), DirectoryRevision = recipient.DirectoryRevision,
            DirectorySourceUpdatedAtUtc = recipient.DirectorySourceUpdatedAtUtc, OnCallSnapshot = recipient.OnCallSnapshot,
            ConfirmedByUserId = plan.ConfirmedByUserId, ConfirmedAtUtc = plan.ConfirmedAtUtc,
        })).ToArray();
}
