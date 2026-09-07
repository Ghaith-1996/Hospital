using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Escalation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AlertEscalationPlanTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Fact]
    public void CanonicalRevisionIgnoresInputOrderButBindsEveryPolicyRuleAndStep()
    {
        var definition = Plan();
        var reordered = definition with { Steps = definition.Steps.Reverse().Select(s => s with { Recipients = s.Recipients.Reverse().ToArray() }).ToArray() };
        AlertEscalationPlan.ComputeRevision(definition).Should().Be(AlertEscalationPlan.ComputeRevision(reordered));
        var changed = definition with { Steps = definition.Steps.Select(s => s with { MaxAttempts = s.MaxAttempts + 1 }).ToArray() };
        AlertEscalationPlan.ComputeRevision(definition).Should().NotBe(AlertEscalationPlan.ComputeRevision(changed));
        AlertEscalationPlan.ComputeRevision(definition).Should().NotBe(AlertEscalationPlan.ComputeRevision(definition with { StopCondition = "DEMO changed stop" }));
        AlertEscalationPlan.ComputeRevision(definition).Should().NotBe(AlertEscalationPlan.ComputeRevision(definition with { PolicyVersion = "DEMO-2" }));
    }

    [Fact]
    public void ConfirmationRoundTripRetainsCanonicalEvidenceAndCannotBeMutatedThroughReturnedArrays()
    {
        var definition = Plan();
        var plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), UserId.New(), Now);
        plan.Definition.Should().BeEquivalentTo(definition);
        var returned = (IList<EscalationStepSnapshot>)plan.Definition.Steps;
        returned[0] = returned[0] with { DelaySeconds = 999 };
        plan.Definition.Steps[0].DelaySeconds.Should().Be(60);
        AlertEscalationPlan.ComputeRevision(plan.Definition).Should().Be(plan.Revision);
        AlertEscalationRecipientSnapshot.FromPlan(plan).Should().HaveCount(3).And.OnlyContain(r => r.PlanRevision == plan.Revision && r.ConfirmedAtUtc == Now);
    }

    [Fact]
    public void DuplicateRecipientChannelAcrossStepsIsRejected()
    {
        var definition = Plan();
        var duplicate = definition with { Steps = [definition.Steps[0], definition.Steps[0] with { Sequence = 2 }] };
        var act = () => AlertEscalationPlan.ComputeRevision(duplicate);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2147483647)]
    public void RetryBoundsAreExplicitlyLimitedForDemo(int attempts)
    {
        var definition = Plan();
        definition = definition with { Steps = definition.Steps.Select(s => s with { MaxAttempts = attempts }).ToArray() };
        var act = () => AlertEscalationPlan.ComputeRevision(definition);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EmptyActorAndNonUtcConfirmationAreRejected()
    {
        var definition = Plan();
        var revision = AlertEscalationPlan.ComputeRevision(definition);
        var empty = () => AlertEscalationPlan.Confirm(definition, revision, new UserId(Guid.Empty), Now);
        empty.Should().Throw<DomainException>();
        var nonUtc = () => AlertEscalationPlan.Confirm(definition, revision, UserId.New(), Now.ToOffset(TimeSpan.FromHours(1)));
        nonUtc.Should().Throw<NonUtcTimestampException>();
    }

    private static EscalationPlanDefinition Plan()
    {
        EscalationRecipientEvidence Recipient() => new(Guid.NewGuid(), Guid.NewGuid(), "Fictional DEMO backup", "DEMO role", "SecureMessage", "SIM-revision", Now, "No assignment");
        return new(Guid.NewGuid(), Guid.NewGuid(), 3, Guid.NewGuid(), "DEMO-1", "DEMO trigger", "DEMO stop", "SIM-primary",
            [new(1, 60, 1, "DEMO-role:one", [Recipient(), Recipient()]), new(2, 120, 1, "DEMO-role:two", [Recipient()])]);
    }
}
