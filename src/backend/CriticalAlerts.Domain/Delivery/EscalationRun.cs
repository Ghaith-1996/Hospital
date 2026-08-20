namespace CriticalAlerts.Domain.Delivery;

public sealed class EscalationRun
{
    private EscalationRun()
    {
        PolicyVersion = string.Empty;
    }

    private EscalationRun(
        EscalationRunId id,
        OrganizationId organizationId,
        AlertId alertId,
        EscalationPolicyId policyId,
        string policyVersion,
        int currentStep,
        DateTimeOffset nextDueAtUtc,
        EscalationRunState state,
        DateTimeOffset startedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        PolicyId = policyId;
        PolicyVersion = policyVersion;
        CurrentStep = currentStep;
        NextDueAtUtc = nextDueAtUtc;
        State = state;
        StartedAtUtc = startedAtUtc;
    }

    public EscalationRunId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public EscalationPolicyId PolicyId { get; private set; }

    public string PolicyVersion { get; private set; }

    public int CurrentStep { get; private set; }

    public DateTimeOffset NextDueAtUtc { get; private set; }

    public EscalationRunState State { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public static EscalationRun Schedule(
        EscalationRunId id,
        OrganizationId organizationId,
        AlertId alertId,
        EscalationPolicyId policyId,
        string policyVersion,
        DateTimeOffset nextDueAtUtc,
        DateTimeOffset startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new DomainException("Escalation runs require a policy version.");
        }

        return new EscalationRun(
            id,
            organizationId,
            alertId,
            policyId,
            policyVersion.Trim(),
            currentStep: 1,
            UtcInstant.Require(nextDueAtUtc, nameof(nextDueAtUtc)),
            EscalationRunState.Scheduled,
            UtcInstant.Require(startedAtUtc, nameof(startedAtUtc)));
    }
}
