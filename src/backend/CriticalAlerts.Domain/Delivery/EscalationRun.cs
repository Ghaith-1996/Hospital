using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Escalation;

namespace CriticalAlerts.Domain.Delivery;

public sealed class EscalationRun
{
    private EscalationRun() { }
    public EscalationRunId Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public EscalationPolicyId PolicyId { get; private set; }
    public string PolicyVersion { get; private set; } = string.Empty;
    // Nullable bindings deliberately leave foundation-era rows ineligible.
    public AlertDraftVersion? AlertVersion { get; private set; }
    public AlertEscalationPlanId? PlanId { get; private set; }
    public string? PlanRevision { get; private set; }
    public int CurrentStep { get; private set; }
    public DateTimeOffset NextDueAtUtc { get; private set; }
    public EscalationRunState State { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public TimeSpan? RemainingDelay { get; private set; }
    public EscalationOutcome? Outcome { get; private set; }
    public EscalationFailureCategory? FailureCategory { get; private set; }
    private readonly List<EscalationConsumedSignal> _consumedSignals = [];
    public IReadOnlyCollection<EscalationConsumedSignal> ConsumedSignals => _consumedSignals.AsReadOnly();

    public static EscalationRun Schedule(EscalationRunId id, AlertEscalationPlan plan, AlertDraftVersion confirmedVersion, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        UtcInstant.Require(now, nameof(now));
        if (id.Value == Guid.Empty || confirmedVersion.Value < 1 || plan.AlertVersion != confirmedVersion.Value || now < plan.ConfirmedAtUtc)
            throw new DomainException("Escalation requires an exact confirmed version and valid scheduling time.");
        return new EscalationRun
        {
            Id = id,
            OrganizationId = plan.OrganizationId,
            AlertId = plan.AlertId,
            AlertVersion = confirmedVersion,
            PlanId = plan.Id,
            PlanRevision = plan.Revision,
            PolicyId = plan.EscalationPolicyId,
            PolicyVersion = plan.EscalationPolicyVersion,
            CurrentStep = 1,
            State = EscalationRunState.Scheduled,
            StartedAtUtc = now,
            UpdatedAtUtc = now,
            NextDueAtUtc = AddDelay(now, plan.Definition.Steps[0].DelaySeconds),
        };
    }

    // All operations require the repository's alert-row-before-run transaction lock.
    // Claiming a paused run allows terminal-fact checks, never step execution.
    public void AcquireLease(string owner, DateTimeOffset now, TimeSpan duration)
    {
        RequireMutable(now);
        RequireOwner(owner);
        if (duration <= TimeSpan.Zero || duration > DateTimeOffset.MaxValue - now)
            throw new DomainException("Escalation leases require a positive representable duration.");
        if (LeaseExpiresAtUtc > now)
            throw new DomainException("The escalation run already has an active lease.");
        LeaseOwner = owner;
        LeaseExpiresAtUtc = now.Add(duration);
        // A recovered in-flight step must pass BeginProcessing again.
        if (State == EscalationRunState.Running) State = EscalationRunState.Scheduled;
        UpdatedAtUtc = now;
    }

    public void BeginProcessing(string owner, DateTimeOffset now)
    {
        RequireLease(owner, now);
        if (State != EscalationRunState.Scheduled || PausedAtUtc is not null || NextDueAtUtc > now)
            throw new DomainException("Only a due unpaused step can begin processing.");
        State = EscalationRunState.Running;
        UpdatedAtUtc = now;
    }

    public void Advance(AlertEscalationPlan plan, int expectedStep, string owner, DateTimeOffset now)
    {
        RequireProcessing(owner, now);
        var steps = RequirePlan(plan);
        if (expectedStep != CurrentStep || CurrentStep > steps.Count)
            throw new DomainException("The escalation step has changed.");
        var nextDue = CurrentStep < steps.Count ? AddDelay(now, steps[CurrentStep].DelaySeconds) : now;
        CurrentStep++;
        UpdatedAtUtc = now;
        if (CurrentStep > steps.Count)
        {
            Complete(plan, owner, now);
            return;
        }
        NextDueAtUtc = nextDue;
        State = EscalationRunState.Scheduled;
    }

    // Human controls are authorized by the service and invalidate any worker claim.
    public void Pause(UserId actor, DateTimeOffset now)
    {
        RequireMutable(now);
        RequireActor(actor);
        if (PausedAtUtc is not null) throw new DomainException("The escalation run is already paused.");
        RemainingDelay = NextDueAtUtc > now ? NextDueAtUtc - now : TimeSpan.Zero;
        PausedAtUtc = now;
        State = EscalationRunState.Paused;
        ClearLease();
        UpdatedAtUtc = now;
    }

    public void Resume(UserId actor, DateTimeOffset now)
    {
        RequireMutable(now);
        RequireActor(actor);
        if (State != EscalationRunState.Paused || RemainingDelay is null || RemainingDelay < TimeSpan.Zero)
            throw new DomainException("Only a paused escalation run can resume.");
        if (RemainingDelay > DateTimeOffset.MaxValue - now)
            throw new DomainException("The remaining escalation delay cannot be represented.");
        NextDueAtUtc = now.Add(RemainingDelay.Value);
        RemainingDelay = null;
        PausedAtUtc = null;
        State = EscalationRunState.Scheduled;
        ClearLease();
        UpdatedAtUtc = now;
    }

    public void Stop(ResponsibilityAssignment assignment, string owner, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        RequireLease(owner, now);
        if (assignment.OrganizationId != OrganizationId || assignment.AlertId != AlertId
            || assignment.AlertVersion != AlertVersion || assignment.ReleasedAtUtc is not null || assignment.AcceptedAtUtc > now)
            throw new DomainException("An active exact-version responsibility assignment is required.");
        Finish(EscalationRunState.Stopped, EscalationOutcome.ResponsibilityAccepted, now);
    }

    public void Stop(AlertState state, string owner, DateTimeOffset now)
    {
        RequireLease(owner, now);
        var outcome = state switch
        {
            AlertState.Resolved => EscalationOutcome.Resolved,
            AlertState.Cancelled => EscalationOutcome.Cancelled,
            _ => throw new DomainException("Only resolution or cancellation stops escalation by lifecycle."),
        };
        Finish(EscalationRunState.Stopped, outcome, now);
    }

    public void Complete(AlertEscalationPlan plan, string owner, DateTimeOffset now)
    {
        RequireProcessing(owner, now);
        if (CurrentStep != RequirePlan(plan).Count + 1)
            throw new DomainException("Automatic escalation steps have not been exhausted.");
        Finish(EscalationRunState.Completed, EscalationOutcome.Exhausted, now);
    }

    public void ReleaseLease(string owner, DateTimeOffset now)
    {
        RequireLease(owner, now);
        ClearLease();
        if (State == EscalationRunState.Running) State = EscalationRunState.Scheduled;
        UpdatedAtUtc = now;
    }

    public void Fail(EscalationFailureCategory category, string owner, DateTimeOffset now)
    {
        RequireProcessing(owner, now);
        if (!Enum.IsDefined(category)) throw new DomainException("An allowlisted escalation failure is required.");
        FailureCategory = category;
        Finish(EscalationRunState.Stopped, EscalationOutcome.ProcessingFailed, now);
    }

    // Repository must load the complete durable consumption collection before mutation.
    public EscalationConsumedSignal ConsumeSignal(RecipientResponse response, AlertRecipientSelection selection, string owner, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(selection);
        RequireLease(owner, now);
        if (State != EscalationRunState.Scheduled || PausedAtUtc is not null
            || response.ResponseType is not (RecipientResponseType.Declined or RecipientResponseType.Unavailable)
            || response.OrganizationId != OrganizationId || response.AlertId != AlertId || response.AlertVersion != AlertVersion
            || selection.OrganizationId != OrganizationId || selection.AlertId != AlertId || selection.AlertVersion != AlertVersion
            || selection.PractitionerId != response.PractitionerId || selection.SelectedAtUtc > response.OccurredAtUtc || response.OccurredAtUtc > now
            || _consumedSignals.Any(signal => signal.ResponseId == response.Id || signal.StepSequence == CurrentStep))
            throw new DomainException("Only an unconsumed exact-version activated recipient signal can accelerate a step.");
        var signal = EscalationConsumedSignal.Create(this, response.Id, selection.Id, now);
        _consumedSignals.Add(signal);
        NextDueAtUtc = now;
        UpdatedAtUtc = now;
        return signal;
    }

    private IReadOnlyList<EscalationStepSnapshot> RequirePlan(AlertEscalationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (PlanId != plan.Id || PlanRevision != plan.Revision || OrganizationId != plan.OrganizationId
            || AlertId != plan.AlertId || AlertVersion?.Value != plan.AlertVersion
            || PolicyId != plan.EscalationPolicyId || PolicyVersion != plan.EscalationPolicyVersion)
            throw new DomainException("The exact immutable escalation plan is required.");
        return plan.Definition.Steps;
    }

    private void RequireMutable(DateTimeOffset now)
    {
        UtcInstant.Require(now, nameof(now));
        if (AlertVersion is null || PlanId is null || string.IsNullOrEmpty(PlanRevision)
            || State is EscalationRunState.Completed or EscalationRunState.Stopped || now < (UpdatedAtUtc ?? StartedAtUtc))
            throw new DomainException("The escalation run is terminal, unbound, or the timestamp is stale.");
    }

    private void RequireLease(string owner, DateTimeOffset now)
    {
        RequireMutable(now);
        RequireOwner(owner);
        if (LeaseOwner != owner || LeaseExpiresAtUtc is null || LeaseExpiresAtUtc <= now)
            throw new DomainException("A current owned escalation lease is required.");
    }

    private void RequireProcessing(string owner, DateTimeOffset now)
    {
        RequireLease(owner, now);
        if (State != EscalationRunState.Running || PausedAtUtc is not null)
            throw new DomainException("The escalation step is not processing.");
    }

    private void Finish(EscalationRunState state, EscalationOutcome outcome, DateTimeOffset now)
    {
        State = state;
        Outcome = outcome;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
        PausedAtUtc = null;
        RemainingDelay = null;
        ClearLease();
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private static void RequireOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 100 || owner.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new DomainException("A safe escalation lease owner is required.");
    }

    private static void RequireActor(UserId actor)
    {
        if (actor.Value == Guid.Empty) throw new DomainException("Escalation controls require an actor.");
    }

    private static DateTimeOffset AddDelay(DateTimeOffset now, long seconds)
    {
        if (seconds < 0 || seconds > (DateTimeOffset.MaxValue - now).TotalSeconds)
            throw new DomainException("The confirmed escalation delay cannot be represented.");
        return now.AddSeconds(seconds);
    }
}
