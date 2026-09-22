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

    public AlertDraftVersion? AlertVersion { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset NextCheckAtUtc { get; private set; }
    public TimeSpan? RemainingDelay { get; private set; }
    public string? StopReason { get; private set; }
    public int HandledNegativeResponses { get; private set; }
    public int EventSequence { get; private set; }

    public EscalationEvent Record(EscalationEventKind kind, DateTimeOffset now, Guid? recipientSelectionId = null, UserId? actor = null)
        => EscalationEvent.Record(Id, OrganizationId, ++EventSequence, kind, CurrentStep, now, recipientSelectionId, actor);

    public EscalationEventKind? Evaluate(AlertState state, bool responsibility, int negativeResponses, DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        if (State is EscalationRunState.Stopped or EscalationRunState.Failed or EscalationRunState.Completed) return null;
        if (state == AlertState.Resolved) return EscalationEventKind.StoppedByResolution;
        if (state == AlertState.Cancelled) return EscalationEventKind.StoppedByCancellation;
        if (responsibility) return EscalationEventKind.StoppedByResponsibility;
        if (State is EscalationRunState.Paused or EscalationRunState.Exhausted || state != AlertState.Active) return null;
        return negativeResponses > HandledNegativeResponses || NextDueAtUtc <= now ? EscalationEventKind.StepDue : null;
    }

    public void Pause(DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        RequireState(EscalationRunState.Scheduled);
        RemainingDelay = NextDueAtUtc > now ? NextDueAtUtc - now : TimeSpan.Zero;
        State = EscalationRunState.Paused;
    }

    public void Resume(DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        RequireState(EscalationRunState.Paused);
        NextDueAtUtc = now.Add(RemainingDelay ?? throw new DomainException("Paused escalation requires a remaining delay."));
        RemainingDelay = null;
        NextCheckAtUtc = now;
        State = EscalationRunState.Scheduled;
    }

    public void Advance(TimeSpan? nextDelay, int negativeResponses, DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        RequireState(EscalationRunState.Scheduled);
        if (nextDelay < TimeSpan.Zero || negativeResponses < HandledNegativeResponses)
            throw new DomainException("Escalation progress cannot regress.");
        HandledNegativeResponses = negativeResponses;
        if (nextDelay is null)
        {
            State = EscalationRunState.Exhausted;
            CompletedAtUtc = now;
        }
        else
        {
            CurrentStep++;
            NextDueAtUtc = now.Add(nextDelay.Value);
        }
    }

    public void Stop(EscalationEventKind reason, DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        if (reason is not (EscalationEventKind.StoppedByResponsibility or EscalationEventKind.StoppedByResolution
            or EscalationEventKind.StoppedByCancellation or EscalationEventKind.ProcessingFailed))
            throw new DomainException("An allowed escalation stop reason is required.");
        State = reason == EscalationEventKind.ProcessingFailed ? EscalationRunState.Failed : EscalationRunState.Stopped;
        StopReason = reason.ToString();
        CompletedAtUtc = now;
        RemainingDelay = null;
    }

    public bool TryAcquireLease(string owner, DateTimeOffset now, TimeSpan duration)
    {
        UtcInstant.Require(now, nameof(now));
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 128 || owner.Any(char.IsControl) || duration <= TimeSpan.Zero)
            throw new DomainException("A safe escalation lease owner and positive duration are required.");
        if (LeaseExpiresAtUtc > now) return false;
        LeaseOwner = owner;
        LeaseExpiresAtUtc = now.Add(duration);
        return true;
    }

    public void ReleaseLease(string owner, DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        if (LeaseOwner != owner || LeaseExpiresAtUtc <= now)
            throw new DomainException("Only the current escalation lease owner may release work.");
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        NextCheckAtUtc = now.AddSeconds(1); // DEMO worker polling, not a clinical escalation interval.
    }

    private void RequireState(EscalationRunState required)
    {
        if (State != required) throw new DomainException("The escalation state does not permit this action.");
    }

    public static EscalationRun Schedule(
        EscalationRunId id,
        OrganizationId organizationId,
        AlertId alertId,
        EscalationPolicyId policyId,
        string policyVersion,
        DateTimeOffset nextDueAtUtc,
        DateTimeOffset startedAtUtc,
        AlertDraftVersion alertVersion)
    {
        if (string.IsNullOrWhiteSpace(policyVersion) || alertVersion.Value <= 0 || nextDueAtUtc < startedAtUtc)
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
            UtcInstant.Require(startedAtUtc, nameof(startedAtUtc)))
        {
            AlertVersion = alertVersion,
            NextCheckAtUtc = startedAtUtc,
        };
    }
}
