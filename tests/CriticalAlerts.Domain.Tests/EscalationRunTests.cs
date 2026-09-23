using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class EscalationRunTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-22T12:00:00Z");

    [Theory]
    [InlineData(AlertState.Resolved, true, EscalationEventKind.StoppedByResolution)]
    [InlineData(AlertState.Cancelled, true, EscalationEventKind.StoppedByCancellation)]
    [InlineData(AlertState.Active, true, EscalationEventKind.StoppedByResponsibility)]
    [InlineData(AlertState.Active, false, EscalationEventKind.StepDue)]
    public void DurableStopsTakePrecedenceOverTimeoutAndDecline(AlertState state, bool responsibility, EscalationEventKind expected)
    {
        var run = Create();
        run.Evaluate(state, responsibility, 1, Now.AddMinutes(2)).Should().Be(expected);
        run.AlertVersion!.Value.Value.Should().Be(7);
    }

    [Fact]
    public void AcknowledgementAndOpeningWithoutResponsibilityDoNotStopTheDeadline()
    {
        var run = Create();
        run.Evaluate(AlertState.Active, false, 0, Now).Should().BeNull();
        run.Evaluate(AlertState.Active, false, 0, Now.AddMinutes(1)).Should().Be(EscalationEventKind.StepDue);
    }

    [Fact]
    public void PauseSurvivesTimeAndResumePreservesOnlyRemainingDelay()
    {
        var run = Create();
        run.Pause(Now.AddSeconds(20));
        run.Evaluate(AlertState.Active, false, 1, Now.AddHours(2)).Should().BeNull();
        run.Resume(Now.AddHours(2));
        run.NextDueAtUtc.Should().Be(Now.AddHours(2).AddSeconds(40));
        run.Evaluate(AlertState.Active, false, 0, Now.AddHours(2).AddSeconds(39)).Should().BeNull();
    }

    [Fact]
    public void DuePauseResumesImmediatelyAndAcceptanceOverridesPause()
    {
        var run = Create();
        run.Pause(Now.AddMinutes(2));
        run.Evaluate(AlertState.Active, true, 1, Now.AddMinutes(3)).Should().Be(EscalationEventKind.StoppedByResponsibility);
        run.Resume(Now.AddMinutes(3));
        run.NextDueAtUtc.Should().Be(Now.AddMinutes(3));
    }

    [Fact]
    public void NegativeResponsesExpediteOnceAndFinalStepExhausts()
    {
        var run = Create();
        run.Evaluate(AlertState.Active, false, 1, Now).Should().Be(EscalationEventKind.StepDue);
        run.Advance(TimeSpan.FromMinutes(2), 1, Now);
        run.Evaluate(AlertState.Active, false, 1, Now.AddSeconds(1)).Should().BeNull();
        run.Evaluate(AlertState.Active, false, 2, Now.AddSeconds(1)).Should().Be(EscalationEventKind.StepDue);
        run.Advance(null, 2, Now.AddSeconds(1));
        run.State.Should().Be(EscalationRunState.Exhausted);
        var resume = () => run.Resume(Now.AddMinutes(1));
        resume.Should().Throw<DomainException>();
    }

    [Fact]
    public void LeaseExpiryAllowsRecoveryButRejectsTheFormerOwner()
    {
        var run = Create();
        run.TryAcquireLease("worker-a", Now, TimeSpan.FromMinutes(1)).Should().BeTrue();
        run.TryAcquireLease("worker-b", Now.AddSeconds(59), TimeSpan.FromMinutes(1)).Should().BeFalse();
        run.TryAcquireLease("worker-b", Now.AddMinutes(1), TimeSpan.FromMinutes(1)).Should().BeTrue();
        var formerOwner = () => run.ReleaseLease("worker-a", Now.AddMinutes(1));
        formerOwner.Should().Throw<DomainException>();
        run.ReleaseLease("worker-b", Now.AddMinutes(1));
        run.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void NonUtcAndIllegalPauseAreRejected()
    {
        var run = Create();
        var invalidTime = () => run.Pause(Now.ToOffset(TimeSpan.FromHours(1)));
        invalidTime.Should().Throw<NonUtcTimestampException>();
        run.Pause(Now);
        var secondPause = () => run.Pause(Now);
        secondPause.Should().Throw<DomainException>();
    }

    private static EscalationRun Create() => EscalationRun.Schedule(
        EscalationRunId.New(), OrganizationId.New(), AlertId.New(), EscalationPolicyId.New(),
        "DEMO-9", Now.AddMinutes(1), Now, new AlertDraftVersion(7));
}
