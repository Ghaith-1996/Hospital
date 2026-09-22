namespace CriticalAlerts.Domain.Delivery;

// An immutable approval artifact. Absence means legacy/non-enabled, never a default policy.
public sealed class ConfirmedEscalationPlan
{
    private ConfirmedEscalationPlan() { }

    public Guid Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public AlertDraftVersion AlertVersion { get; private set; }
    public EscalationPolicyId PolicyId { get; private set; }
    public string PolicyVersion { get; private set; } = string.Empty;
    public string Revision { get; private set; } = string.Empty;
    public string SnapshotJson { get; private set; } = string.Empty;
    public UserId ConfirmedByUserId { get; private set; }
    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    public static ConfirmedEscalationPlan Capture(
        OrganizationId organizationId, AlertId alertId, AlertDraftVersion alertVersion,
        EscalationPolicyId policyId, string policyVersion, string revision, string snapshotJson,
        UserId actor, DateTimeOffset now)
    {
        if (!policyVersion.StartsWith("DEMO", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new DomainException("An exact DEMO escalation approval is required.");
        }

        return new ConfirmedEscalationPlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AlertId = alertId,
            AlertVersion = alertVersion,
            PolicyId = policyId,
            PolicyVersion = policyVersion,
            Revision = revision,
            SnapshotJson = snapshotJson,
            ConfirmedByUserId = actor,
            ConfirmedAtUtc = UtcInstant.Require(now, nameof(now)),
        };
    }
}
