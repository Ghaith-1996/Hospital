namespace CriticalAlerts.Domain.Alerts;

public sealed class AlertStateTransition
{
    private AlertStateTransition()
    {
        ReasonCode = string.Empty;
        CorrelationId = string.Empty;
        PolicyVersion = string.Empty;
    }

    internal AlertStateTransition(
        AlertStateTransitionId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertState fromState,
        AlertState toState,
        UserId? actorUserId,
        string reasonCode,
        string correlationId,
        string policyVersion,
        DateTimeOffset occurredAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        FromState = fromState;
        ToState = toState;
        ActorUserId = actorUserId;
        ReasonCode = reasonCode;
        CorrelationId = correlationId;
        PolicyVersion = policyVersion;
        OccurredAtUtc = occurredAtUtc;
    }

    public AlertStateTransitionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertState FromState { get; private set; }

    public AlertState ToState { get; private set; }

    public UserId? ActorUserId { get; private set; }

    public string ReasonCode { get; private set; }

    public string CorrelationId { get; private set; }

    public string PolicyVersion { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }
}
