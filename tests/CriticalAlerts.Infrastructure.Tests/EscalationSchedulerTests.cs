using System.Text;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationSchedulerTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task BoundedWorkerDiscoverySchedulesEligibleAlerts()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var scheduler = new EscalationScheduler(db);
        var found = false;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            if (await db.EscalationRuns.AnyAsync(r => r.AlertId == id)) { found = true; break; }
            (await scheduler.ScheduleNextAsync()).Should().BeTrue();
        }
        found.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentSchedulingCreatesOneRunEventAndAuditWithDatabaseDeadline()
    {
        var id = await CreateAlert();
        await using var left = fixture.CreateContext();
        await using var right = fixture.CreateContext();
        var before = await new DatabaseClock(left).GetUtcNowAsync();
        var outcomes = await Task.WhenAll(new EscalationScheduler(left).ScheduleAsync(DemoDataSeeder.OrganizationId, id),
            new EscalationScheduler(right).ScheduleAsync(DemoDataSeeder.OrganizationId, id));
        outcomes.Count(x => x).Should().Be(1);
        await using var reload = fixture.CreateContext();
        var after = await new DatabaseClock(reload).GetUtcNowAsync();
        var run = await reload.EscalationRuns.SingleAsync(x => x.AlertId == id);
        run.StartedAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        run.NextDueAtUtc.Should().Be(run.StartedAtUtc.AddSeconds(60));
        (await reload.EscalationEvents.CountAsync(x => x.RunId == run.Id)).Should().Be(1);
        (await reload.AuditEvents.CountAsync(x => x.ResourceId == run.Id.Value && x.Action == "escalation-scheduled")).Should().Be(1);
        (await reload.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
    }

    [Theory]
    [InlineData(AlertState.Draft)]
    [InlineData(AlertState.PendingConfirmation)]
    [InlineData(AlertState.DispatchQueued)]
    [InlineData(AlertState.Failed)]
    [InlineData(AlertState.Resolved)]
    [InlineData(AlertState.Cancelled)]
    public async Task OnlyActiveAlertsCanSchedule(AlertState state)
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alerts SET state = {state.ToString()} WHERE id = {id.Value}");
        (await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id)).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyAndUnsupportedExactPlansAreNotScheduled()
    {
        var legacy = await CreateAlert(bind: false);
        var unsupported = await CreateAlert(supported: false);
        await using var db = fixture.CreateContext();
        var scheduler = new EscalationScheduler(db);
        (await scheduler.ScheduleAsync(DemoDataSeeder.OrganizationId, legacy)).Should().BeFalse();
        (await scheduler.ScheduleAsync(DemoDataSeeder.OrganizationId, unsupported)).Should().BeFalse();
        var plan = await db.AlertEscalationPlans.SingleAsync(p => p.AlertId == unsupported);
        var run = EscalationRun.Schedule(EscalationRunId.New(), plan, new(plan.AlertVersion), await new DatabaseClock(db).GetUtcNowAsync());
        db.EscalationRuns.Add(run);
        await db.SaveChangesAsync();
        await MakeDue(db, run.Id);
        var repository = new EscalationRunRepository(db);
        (await repository.FindCandidatesAsync()).Should().NotContain(run.Id);
        (await repository.TryClaimAsync(run.Id, "unsupported-process", TimeSpan.FromSeconds(30))).Should().BeNull();
    }

    [Fact]
    public async Task ClaimsUseDatabaseDueTimeAndRecoveredTokenFencesEvenSameProcessOwner()
    {
        var id = await CreateAlert();
        EscalationRunId runId;
        DateTimeOffset deadline;
        await using (var db = fixture.CreateContext())
        {
            await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
            var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
            runId = run.Id;
            deadline = run.NextDueAtUtc;
            (await new EscalationRunRepository(db).TryClaimAsync(runId, "same-process", TimeSpan.FromSeconds(30))).Should().BeNull();
            await MakeDue(db, runId);
        }
        await using var left = fixture.CreateContext();
        await using var right = fixture.CreateContext();
        var claims = await Task.WhenAll(new EscalationRunRepository(left).TryClaimAsync(runId, "same-process", TimeSpan.FromSeconds(30)),
            new EscalationRunRepository(right).TryClaimAsync(runId, "same-process", TimeSpan.FromSeconds(30)));
        var first = claims.Single(x => x is not null)!;
        await using var restarted = fixture.CreateContext();
        await restarted.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET lease_expires_at_utc = clock_timestamp() - interval '1 second' WHERE id = {runId.Value}");
        var repository = new EscalationRunRepository(restarted);
        var second = (await repository.TryClaimAsync(runId, "same-process", TimeSpan.FromSeconds(30)))!;
        second.LeaseOwner.Should().NotBe(first.LeaseOwner);
        var invoked = false;
        (await repository.ExecuteClaimAsync(first, (_, _) => { invoked = true; return Task.CompletedTask; })).Should().BeFalse();
        invoked.Should().BeFalse();
        (await repository.ExecuteClaimAsync(second, (locked, _) =>
        {
            locked.Run.BeginProcessing(second.LeaseOwner, locked.Now);
            locked.Run.ReleaseLease(second.LeaseOwner, locked.Now);
            return Task.CompletedTask;
        })).Should().BeTrue();
        var loaded = await restarted.EscalationRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
        loaded.StartedAtUtc.AddSeconds(60).Should().Be(deadline);
        loaded.CurrentStep.Should().Be(1);
    }

    [Fact]
    public async Task PausedRunsWaitButLifecycleStopFactsClaimBeforeDeadline()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        run.Pause(DemoDataSeeder.JordanUserId, await new DatabaseClock(db).GetUtcNowAsync());
        await db.SaveChangesAsync();
        var repository = new EscalationRunRepository(db);
        (await repository.FindCandidatesAsync()).Should().NotContain(run.Id);
        (await repository.TryClaimAsync(run.Id, "demo-process", TimeSpan.FromSeconds(30))).Should().BeNull();
        var alert = await db.Alerts.SingleAsync(x => x.Id == id);
        alert.Cancel(DemoDataSeeder.JordanUserId, await new DatabaseClock(db).GetUtcNowAsync(), "test-cancel");
        await db.SaveChangesAsync();
        (await repository.FindCandidatesAsync()).Should().Contain(run.Id);
        (await repository.TryClaimAsync(run.Id, "demo-process", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    }

    [Fact]
    public async Task SuccessfulNonterminalAdvanceConsumesClaimEvenWithZeroDelayNextStep()
    {
        var id = await CreateAlert(twoSteps: true);
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        await MakeDue(db, run.Id);
        var repository = new EscalationRunRepository(db);
        var claim = (await repository.TryClaimAsync(run.Id, "single-use-process", TimeSpan.FromSeconds(30)))!;
        (await repository.ExecuteClaimAsync(claim, (locked, _) =>
        {
            locked.Run.BeginProcessing(claim.LeaseOwner, locked.Now);
            locked.Run.Advance(locked.Plan, 1, claim.LeaseOwner, locked.Now);
            return Task.CompletedTask;
        })).Should().BeTrue();
        run.CurrentStep.Should().Be(2);
        var replayed = false;
        (await repository.ExecuteClaimAsync(claim, (_, _) => { replayed = true; return Task.CompletedTask; })).Should().BeFalse();
        replayed.Should().BeFalse();
    }

    [Fact]
    public async Task SlowCallbackCannotCommitAfterLeaseExpiryEvenWhenItClearedLease()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        await MakeDue(db, run.Id);
        var repository = new EscalationRunRepository(db);
        var claim = (await repository.TryClaimAsync(run.Id, "slow-process", TimeSpan.FromSeconds(2)))!;
        var execute = () => repository.ExecuteClaimAsync(claim, async (locked, cancellation) =>
        {
            locked.Run.BeginProcessing(claim.LeaseOwner, locked.Now);
            db.EscalationEvents.Add(EscalationEvent.Record(locked.Run, EscalationEventType.StepDue, 1, null, Guid.NewGuid(), locked.Now));
            locked.Run.ReleaseLease(claim.LeaseOwner, locked.Now);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(2.1)", cancellation);
        });
        await execute.Should().ThrowAsync<DomainException>().WithMessage("*expired*");
        await using var reload = fixture.CreateContext();
        (await reload.EscalationEvents.CountAsync(x => x.RunId == run.Id && x.EventType == EscalationEventType.StepDue)).Should().Be(0);
        var retained = await reload.EscalationRuns.SingleAsync(x => x.Id == run.Id);
        retained.LeaseOwner.Should().Be(claim.LeaseOwner);
        retained.State.Should().Be(EscalationRunState.Scheduled);
    }

    [Theory]
    [InlineData(RecipientResponseType.Acknowledged, false, false)]
    [InlineData(RecipientResponseType.CallUnitRequested, false, false)]
    [InlineData(RecipientResponseType.Declined, false, true)]
    [InlineData(RecipientResponseType.Unavailable, false, true)]
    [InlineData(RecipientResponseType.Accepted, false, false)]
    [InlineData(RecipientResponseType.Accepted, true, true)]
    public async Task OnlyEligibleSignalsOrActualResponsibilityClaimBeforeDeadline(RecipientResponseType type, bool assign, bool eligible)
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        var selection = await db.AlertRecipientSelections.FirstAsync(x => x.AlertId == id);
        var now = await new DatabaseClock(db).GetUtcNowAsync();
        var response = RecipientResponse.Record(RecipientResponseId.New(), run.OrganizationId, id, run.AlertVersion!.Value,
            selection.PractitionerId, type, DemoDataSeeder.JordanUserId, now, RecipientResponse.DefaultReasonCode(type));
        db.RecipientResponses.Add(response);
        if (assign)
        {
            db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
            run.Pause(DemoDataSeeder.JordanUserId, now);
        }
        await db.SaveChangesAsync();
        var repository = new EscalationRunRepository(db);
        (await repository.FindCandidatesAsync()).Contains(run.Id).Should().Be(eligible);
        var claim = await repository.TryClaimAsync(run.Id, "response-process", TimeSpan.FromSeconds(30));
        (claim is not null).Should().Be(eligible);
        run.ConsumedSignals.Should().BeEmpty("discovery and claims must never consume a signal");
    }

    [Fact]
    public async Task SupportedConfirmedPlanSurvivesMutablePolicyChangesAndRestartDoesNotResetDeadline()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var policy = await db.EscalationPolicies.FirstAsync(x => x.OrganizationId == DemoDataSeeder.OrganizationId);
        var oldTrigger = policy.TriggerCondition;
        await db.EscalationPolicies.Where(x => x.Id == policy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TriggerCondition, "unsupported later trigger"));
        try
        {
            (await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id)).Should().BeTrue();
            var run = await db.EscalationRuns.AsNoTracking().SingleAsync(x => x.AlertId == id);
            await using var restart = fixture.CreateContext();
            (await new EscalationScheduler(restart).ScheduleAsync(DemoDataSeeder.OrganizationId, id)).Should().BeFalse();
            (await restart.EscalationRuns.SingleAsync(x => x.Id == run.Id)).NextDueAtUtc.Should().Be(run.NextDueAtUtc);
            await MakeDue(restart, run.Id);
            (await new EscalationRunRepository(restart).TryClaimAsync(run.Id, "restart-process", TimeSpan.FromSeconds(30))).Should().NotBeNull();
        }
        finally
        {
            await db.EscalationPolicies.Where(x => x.Id == policy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TriggerCondition, oldTrigger));
        }
    }

    [Fact]
    public async Task CallbackFailureRollsBackRunEventsAndClockIsReadAfterAlertLock()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        await MakeDue(db, run.Id);
        var repository = new EscalationRunRepository(db);
        var claim = (await repository.TryClaimAsync(run.Id, "demo-process", TimeSpan.FromSeconds(30)))!;
        await using var blocker = fixture.CreateContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM alerts WHERE id = {id.Value} FOR UPDATE");
        var invoked = false;
        var releasedAt = DateTimeOffset.MinValue;
        var operation = repository.ExecuteClaimAsync(claim, (locked, _) =>
        {
            invoked = true;
            locked.Now.Should().BeOnOrAfter(releasedAt);
            locked.Run.BeginProcessing(claim.LeaseOwner, locked.Now);
            db.EscalationEvents.Add(EscalationEvent.Record(locked.Run, EscalationEventType.StepDue, 1, null, Guid.NewGuid(), locked.Now));
            throw new InvalidOperationException("synthetic rollback");
        });
        // The execution lock deliberately waits for the authoritative alert, unlike discovery.
        await Task.Delay(100);
        invoked.Should().BeFalse();
        releasedAt = await new DatabaseClock(blocker).GetUtcNowAsync();
        await transaction.CommitAsync();
        var execute = () => operation;
        await execute.Should().ThrowAsync<InvalidOperationException>();
        await using var reload = fixture.CreateContext();
        (await reload.EscalationEvents.CountAsync(x => x.RunId == run.Id && x.EventType == EscalationEventType.StepDue)).Should().Be(0);
        (await reload.EscalationRuns.SingleAsync(x => x.Id == run.Id)).State.Should().Be(EscalationRunState.Scheduled);
    }

    private static Task MakeDue(CriticalAlertsDbContext db, EscalationRunId id)
        => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 second' WHERE id = {id.Value}");

    private async Task<AlertId> CreateAlert(bool bind = true, bool supported = true, bool twoSteps = false)
    {
        await using var db = fixture.CreateContext();
        var now = (await new DatabaseClock(db).GetUtcNowAsync()).AddMinutes(-5);
        static ProtectedValue Protect(string value, string purpose = "scheduler-test") => new(Encoding.UTF8.GetBytes(value), "test-v1", purpose);
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId, "SIM-PAT-SCHEDULER", Protect("SIM-PAT-SCHEDULER", ProtectedValuePurposes.AlertPatientReference),
            "SIMULATION room", "DEMO-URGENT", AlertSourceType.Typed, Protect("SIMULATION source"), now,
            Protect("{\"situation\":\"SIMULATION scheduler\"}"));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var role = await db.PractitionerRoles.FirstAsync(x => x.OrganizationId == alert.OrganizationId);
        var practitioner = await db.Practitioners.SingleAsync(x => x.Id == role.PractitionerId);
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.SetApprovedMessage(Protect("SIMULATION message"), alert.DraftVersion, now);
        alert.ReplaceRecipients([new(practitioner.Id, role.Id, NotificationChannel.SecureMessage, "DEMO-revision", now, "No assignment")], DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion, [practitioner], now, "scheduler-test");
        if (bind)
        {
            var policy = await db.EscalationPolicies.FirstAsync(x => x.OrganizationId == alert.OrganizationId);
            List<EscalationStepSnapshot> steps = [new(1, 60, 1, "DEMO backup", [new(practitioner.Id.Value, role.Id.Value, "Fictional DEMO", "DEMO role", "SecureMessage", "DEMO-revision", now, "No assignment")])];
            if (twoSteps) steps.Add(new(2, 0, 1, "DEMO backup", [new(practitioner.Id.Value, role.Id.Value, "Fictional DEMO", "DEMO role", "Sms", "DEMO-revision", now, "No assignment")]));
            var definition = new EscalationPlanDefinition(alert.OrganizationId.Value, alert.Id.Value, alert.DraftVersion.Value, policy.Id.Value, policy.Version,
                supported ? DemoEscalationSemantics.TriggerCondition : "unsupported trigger", DemoEscalationSemantics.StopCondition, "DEMO-primary",
                steps);
            var plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), DemoDataSeeder.JordanUserId, now);
            alert.BindExactEscalationPlan(plan);
            db.AlertEscalationPlans.Add(plan);
        }
        alert.MarkActive(now, "scheduler-test");
        await db.SaveChangesAsync();
        return alert.Id;
    }
}
