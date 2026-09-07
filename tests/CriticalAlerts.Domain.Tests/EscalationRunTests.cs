using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class EscalationRunTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
    private const string Owner = "demo-worker-1";

    [Fact]
    public void ScheduleBindsExactPlanAndConfirmedVersionAndUsesFirstDelay()
    {
        var plan = Plan();
        var run = Schedule(plan);
        run.AlertVersion.Should().Be(new AlertDraftVersion(3));
        run.PlanId.Should().Be(plan.Id);
        run.PlanRevision.Should().Be(plan.Revision);
        run.PolicyId.Should().Be(plan.EscalationPolicyId);
        run.PolicyVersion.Should().Be("DEMO-1");
        run.NextDueAtUtc.Should().Be(Now.AddSeconds(60));
        var stale = () => EscalationRun.Schedule(EscalationRunId.New(), plan, new(2), Now);
        stale.Should().Throw<DomainException>();
    }

    [Fact]
    public void StepAdvanceRequiresDueProcessingExactPlanAndExpectedStep()
    {
        var plan = Plan();
        var run = Schedule(plan);
        Claim(run);
        var early = () => run.BeginProcessing(Owner, Now);
        early.Should().Throw<DomainException>();
        run.BeginProcessing(Owner, Now.AddSeconds(60));
        var otherPlan = () => run.Advance(Plan(), 1, Owner, Now.AddSeconds(60));
        otherPlan.Should().Throw<DomainException>();
        var skipped = () => run.Advance(plan, 2, Owner, Now.AddSeconds(60));
        skipped.Should().Throw<DomainException>();
        run.Advance(plan, 1, Owner, Now.AddSeconds(60));
        run.CurrentStep.Should().Be(2);
        run.NextDueAtUtc.Should().Be(Now.AddSeconds(180));
        var repeat = () => run.Advance(plan, 1, Owner, Now.AddSeconds(60));
        repeat.Should().Throw<DomainException>();
        run.BeginProcessing(Owner, Now.AddSeconds(180));
        run.Advance(plan, 2, Owner, Now.AddSeconds(180));
        run.State.Should().Be(EscalationRunState.Completed);
        run.Outcome.Should().Be(EscalationOutcome.Exhausted);
        run.CompletedAtUtc.Should().Be(Now.AddSeconds(180));
        var terminal = () => run.BeginProcessing(Owner, Now.AddSeconds(181));
        terminal.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(20, 40)]
    [InlineData(60, 0)]
    [InlineData(90, 0)]
    public void PausePreservesRemainingDelayAndResumeRestoresIt(int pauseSeconds, int remainingSeconds)
    {
        var run = Schedule(Plan());
        Claim(run);
        run.Pause(UserId.New(), Now.AddSeconds(pauseSeconds));
        run.RemainingDelay.Should().Be(TimeSpan.FromSeconds(remainingSeconds));
        var paused = () => run.BeginProcessing(Owner, Now.AddSeconds(100));
        paused.Should().Throw<DomainException>();
        run.Resume(UserId.New(), Now.AddSeconds(110));
        run.NextDueAtUtc.Should().Be(Now.AddSeconds(110 + remainingSeconds));
        run.PausedAtUtc.Should().BeNull();
        run.RemainingDelay.Should().BeNull();
    }

    [Fact]
    public void LeaseRejectsBlankOwnersNonpositiveDurationStaleOwnersAndExpiredResults()
    {
        var run = Schedule(Plan());
        var blank = () => run.AcquireLease(" ", Now, TimeSpan.FromMinutes(1));
        blank.Should().Throw<DomainException>();
        var zero = () => run.AcquireLease(Owner, Now, TimeSpan.Zero);
        zero.Should().Throw<DomainException>();
        run.AcquireLease(Owner, Now, TimeSpan.FromSeconds(60));
        var steal = () => run.AcquireLease("worker-2", Now, TimeSpan.FromMinutes(1));
        steal.Should().Throw<DomainException>();
        var expired = () => run.BeginProcessing(Owner, Now.AddSeconds(60));
        expired.Should().Throw<DomainException>();
        run.AcquireLease("worker-2", Now.AddSeconds(60), TimeSpan.FromMinutes(1));
        var stale = () => run.BeginProcessing(Owner, Now.AddSeconds(60));
        stale.Should().Throw<DomainException>();
        var release = () => run.ReleaseLease(Owner, Now.AddSeconds(60));
        release.Should().Throw<DomainException>();
        run.ReleaseLease("worker-2", Now.AddSeconds(60));
        run.LeaseOwner.Should().BeNull();
    }

    [Theory]
    [InlineData(RecipientResponseType.Declined)]
    [InlineData(RecipientResponseType.Unavailable)]
    public void SignalAcceleratesOneStepOnlyAndPreservesExactCorrelation(RecipientResponseType type)
    {
        var plan = Plan();
        var run = Schedule(plan);
        Claim(run);
        var selection = Selection(run);
        var response = Response(run, selection.PractitionerId, type);
        var signal = run.ConsumeSignal(response, selection, Owner, Now);
        signal.AlertVersion.Should().Be(new AlertDraftVersion(3));
        signal.OrganizationId.Should().Be(run.OrganizationId);
        signal.RunId.Should().Be(run.Id);
        signal.ResponseId.Should().Be(response.Id);
        signal.StepSequence.Should().Be(1);
        run.NextDueAtUtc.Should().Be(Now);
        run.BeginProcessing(Owner, Now);
        run.Advance(plan, 1, Owner, Now);
        var reuse = () => run.ConsumeSignal(response, selection, Owner, Now);
        reuse.Should().Throw<DomainException>();
        run.NextDueAtUtc.Should().Be(Now.AddSeconds(120));
    }

    [Theory]
    [InlineData(RecipientResponseType.Acknowledged)]
    [InlineData(RecipientResponseType.CallUnitRequested)]
    [InlineData(RecipientResponseType.Accepted)]
    public void ResponseAloneCannotAccelerateOrStop(RecipientResponseType type)
    {
        var run = Schedule(Plan());
        Claim(run);
        var selection = Selection(run);
        var response = Response(run, selection.PractitionerId, type);
        var consume = () => run.ConsumeSignal(response, selection, Owner, Now);
        consume.Should().Throw<DomainException>();
        run.State.Should().Be(EscalationRunState.Scheduled);
    }

    [Fact]
    public void SignalsRejectPausedForeignVersionForeignRecipientAndMultipleSignalsForSameStep()
    {
        var run = Schedule(Plan());
        Claim(run);
        var selection = Selection(run);
        var response = Response(run, selection.PractitionerId, RecipientResponseType.Declined);
        run.Pause(UserId.New(), Now);
        var paused = () => run.ConsumeSignal(response, selection, Owner, Now);
        paused.Should().Throw<DomainException>();
        run.Resume(UserId.New(), Now);
        Claim(run);
        var foreign = () => run.ConsumeSignal(Response(run, PractitionerId.New(), RecipientResponseType.Declined), selection, Owner, Now);
        foreign.Should().Throw<DomainException>();
        var old = RecipientResponse.Record(RecipientResponseId.New(), run.OrganizationId, run.AlertId, new(2), selection.PractitionerId, RecipientResponseType.Declined, UserId.New(), Now, "simulation-declined");
        var oldVersion = () => run.ConsumeSignal(old, selection, Owner, Now);
        oldVersion.Should().Throw<DomainException>();
        run.ConsumeSignal(response, selection, Owner, Now);
        var second = () => run.ConsumeSignal(Response(run, selection.PractitionerId, RecipientResponseType.Unavailable), selection, Owner, Now);
        second.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResponsibilityStopsPausedRunWithoutExecutingStepAndRejectsOtherVersion()
    {
        var run = Schedule(Plan());
        Claim(run);
        run.Pause(UserId.New(), Now);
        var response = Response(run, PractitionerId.New(), RecipientResponseType.Accepted);
        var assignment = ResponsibilityAssignment.FromResponse(response)!;
        Claim(run);
        run.Stop(assignment, Owner, Now);
        run.State.Should().Be(EscalationRunState.Stopped);
        run.Outcome.Should().Be(EscalationOutcome.ResponsibilityAccepted);
        var process = () => run.BeginProcessing(Owner, Now.AddMinutes(1));
        process.Should().Throw<DomainException>();
        var otherRun = Schedule(Plan());
        Claim(otherRun);
        var foreign = () => otherRun.Stop(assignment, Owner, Now);
        foreign.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(AlertState.Resolved, EscalationOutcome.Resolved)]
    [InlineData(AlertState.Cancelled, EscalationOutcome.Cancelled)]
    public void LifecycleStopWorksWhilePaused(AlertState state, EscalationOutcome outcome)
    {
        var run = Schedule(Plan());
        Claim(run);
        run.Pause(UserId.New(), Now);
        Claim(run);
        run.Stop(state, Owner, Now);
        run.Outcome.Should().Be(outcome);
        run.CompletedAtUtc.Should().Be(Now);
        run.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void InvalidStopPrematureCompletionAndUnsafeEventValuesAreRejected()
    {
        var run = Schedule(Plan());
        Claim(run);
        var stop = () => run.Stop(AlertState.Active, Owner, Now);
        stop.Should().Throw<DomainException>();
        var complete = () => run.Complete(Plan(), Owner, Now);
        complete.Should().Throw<DomainException>();
        var invalid = () => EscalationEvent.Record(run, (EscalationEventType)999, 1, null, Guid.NewGuid(), Now);
        invalid.Should().Throw<DomainException>();
        var emptyCorrelation = () => EscalationEvent.Record(run, EscalationEventType.Scheduled, 1, null, Guid.Empty, Now);
        emptyCorrelation.Should().Throw<DomainException>();
        var recorded = EscalationEvent.Record(run, EscalationEventType.Scheduled, 1, null, Guid.NewGuid(), Now);
        recorded.AlertVersion.Should().Be(new AlertDraftVersion(3));
        recorded.RunId.Should().Be(run.Id);
        run.BeginProcessing(Owner, Now.AddSeconds(60));
        run.Fail(EscalationFailureCategory.ConfirmedRecipientUnavailable, Owner, Now.AddSeconds(60));
        run.Outcome.Should().Be(EscalationOutcome.ProcessingFailed);
        run.FailureCategory.Should().Be(EscalationFailureCategory.ConfirmedRecipientUnavailable);
    }

    [Fact]
    public void EveryTimeBoundaryRequiresUtcAndRejectsBackwardsMutation()
    {
        var plan = Plan();
        var nonUtc = Now.ToOffset(TimeSpan.FromHours(1));
        var schedule = () => EscalationRun.Schedule(EscalationRunId.New(), plan, new(3), nonUtc);
        schedule.Should().Throw<NonUtcTimestampException>();
        var run = Schedule(plan);
        var claim = () => run.AcquireLease(Owner, nonUtc, TimeSpan.FromMinutes(1));
        claim.Should().Throw<NonUtcTimestampException>();
        Claim(run);
        var pause = () => run.Pause(UserId.New(), nonUtc);
        pause.Should().Throw<NonUtcTimestampException>();
        run.Pause(UserId.New(), Now.AddSeconds(20));
        var backwards = () => run.Resume(UserId.New(), Now);
        backwards.Should().Throw<DomainException>();
    }

    [Fact]
    public void HumanPauseInvalidatesClaimAndResumeRequiresFreshWorkerLease()
    {
        var run = Schedule(Plan());
        Claim(run);
        run.BeginProcessing(Owner, Now.AddSeconds(60));
        run.Pause(UserId.New(), Now.AddSeconds(60));
        run.LeaseOwner.Should().BeNull();
        var staleResult = () => run.Fail(EscalationFailureCategory.ProcessingError, Owner, Now.AddSeconds(60));
        staleResult.Should().Throw<DomainException>();
        run.Resume(UserId.New(), Now.AddSeconds(70));
        var oldClaim = () => run.BeginProcessing(Owner, Now.AddSeconds(70));
        oldClaim.Should().Throw<DomainException>();
        run.AcquireLease("demo-worker-2", Now.AddSeconds(70), TimeSpan.FromMinutes(1));
        run.BeginProcessing("demo-worker-2", Now.AddSeconds(70));
        run.State.Should().Be(EscalationRunState.Running);
    }

    [Fact]
    public void EventsRequireUtcActorAndTypedRecipientOrFailureEvidence()
    {
        var run = Schedule(Plan());
        var nonUtc = () => EscalationEvent.Record(run, EscalationEventType.Scheduled, 1, null, Guid.NewGuid(), Now.ToOffset(TimeSpan.FromHours(1)));
        nonUtc.Should().Throw<NonUtcTimestampException>();
        var noActor = () => EscalationEvent.Record(run, EscalationEventType.Paused, 1, null, Guid.NewGuid(), Now);
        noActor.Should().Throw<DomainException>();
        var noRecipient = () => EscalationEvent.Record(run, EscalationEventType.RecipientActivated, 1, null, Guid.NewGuid(), Now);
        noRecipient.Should().Throw<DomainException>();
        var unknownFailure = () => EscalationEvent.Record(run, EscalationEventType.ProcessingFailed, 1, null, Guid.NewGuid(), Now, failureCategory: (EscalationFailureCategory)999);
        unknownFailure.Should().Throw<DomainException>();
        var failure = EscalationEvent.Record(run, EscalationEventType.ProcessingFailed, 1, null, Guid.NewGuid(), Now, failureCategory: EscalationFailureCategory.ConfirmedRecipientUnavailable);
        failure.FailureCategory.Should().Be(EscalationFailureCategory.ConfirmedRecipientUnavailable);
        var selectionId = AlertRecipientSelectionId.New();
        var activation = EscalationEvent.Record(run, EscalationEventType.RecipientActivated, 1, null, Guid.NewGuid(), Now, selectionId);
        activation.RecipientSelectionId.Should().Be(selectionId);
        activation.OrganizationId.Should().Be(run.OrganizationId);
        activation.AlertId.Should().Be(run.AlertId);
    }
    private static void Claim(EscalationRun run) => run.AcquireLease(Owner, Now, TimeSpan.FromMinutes(10));
    private static EscalationRun Schedule(AlertEscalationPlan plan) => EscalationRun.Schedule(EscalationRunId.New(), plan, new(3), Now);
    private static AlertRecipientSelection Selection(EscalationRun run) => new(AlertRecipientSelectionId.New(), run.OrganizationId, run.AlertId, new(3), PractitionerId.New(), null, NotificationChannel.SecureMessage, UserId.New(), Now, "DEMO-revision", null, null);
    private static RecipientResponse Response(EscalationRun run, PractitionerId practitioner, RecipientResponseType type) => RecipientResponse.Record(RecipientResponseId.New(), run.OrganizationId, run.AlertId, new(3), practitioner, type, UserId.New(), Now, RecipientResponse.DefaultReasonCode(type));
    private static AlertEscalationPlan Plan()
    {
        EscalationRecipientEvidence Recipient() => new(Guid.NewGuid(), Guid.NewGuid(), "Fictional DEMO backup", "DEMO role", "SecureMessage", "DEMO-revision", Now, "No assignment");
        var definition = new EscalationPlanDefinition(Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), "DEMO-1", "DEMO trigger", "DEMO stop", "DEMO-primary", [new(1, 60, 1, "DEMO-role:one", [Recipient()]), new(2, 120, 1, "DEMO-role:two", [Recipient()])]);
        return AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), UserId.New(), Now);
    }
}
