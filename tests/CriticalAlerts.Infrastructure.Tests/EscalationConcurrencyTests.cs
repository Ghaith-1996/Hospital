using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Escalation;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Responses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationConcurrencyTests(MigratedPostgresFixture fixture)
{
    [Theory]
    [InlineData("Accepted")]
    [InlineData("Declined")]
    [InlineData("Unavailable")]
    public async Task ActivationWinningAlertLockCommitsBeforeNewBackupResponse(string type)
    {
        await fixture.ResetAsync();
        var id = await new EscalationActivationTests(fixture).CreateAlert(twoSteps: true);
        EscalationClaim claim;
        int version;
        await using (var setup = fixture.CreateContext())
        {
            var backup = await setup.AlertEscalationRecipientSnapshots.SingleAsync(x => x.AlertId == id && x.StepSequence == 1);
            await setup.PractitionerUserLinks.Where(x => x.UserId == DemoDataSeeder.RileyUserId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PractitionerId, backup.PractitionerId));
            version = (await setup.Alerts.SingleAsync(x => x.Id == id)).DraftVersion.Value;
            claim = await Claim(setup, id);
        }
        var gate = new PauseAfterSave();
        await using var workerDb = Gated(gate);
        var activation = new EscalationRunProcessor(workerDb).ProcessClaimAsync(claim);
        await gate.Saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var responseDb = fixture.CreateContext();
        await responseDb.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)responseDb.Database.GetDbConnection()).ProcessID;
        var service = new RecipientResponseService(responseDb, new PractitionerIdentityResolver(responseDb));
        var response = service.RecordAsync(DemoDataSeeder.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-race", id,
            new RecordRecipientResponseRequest(version, type), "SIM-new-backup", default);
        try { await WaitForLock(response, pid); }
        finally { gate.Release.TrySetResult(); }
        (await activation).Should().BeTrue();
        (await response).Should().NotBeNull();
        await using var verify = fixture.CreateContext();
        var recorded = await verify.RecipientResponses.SingleAsync(x => x.AlertId == id);
        var selected = await verify.AlertRecipientSelections.SingleAsync(x => x.AlertId == id && x.PractitionerId == recorded.PractitionerId);
        recorded.OccurredAtUtc.Should().BeOnOrAfter(selected.SelectedAtUtc);
        var next = await Claim(verify, id, due: false);
        await new EscalationRunProcessor(verify).ProcessClaimAsync(next);
        if (type == "Accepted")
        {
            (await verify.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(EscalationOutcome.ResponsibilityAccepted);
            (await verify.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(2);
        }
        else
        {
            (await verify.EscalationConsumedSignals.SingleAsync(x => x.AlertId == id)).ResponseId.Should().Be(recorded.Id);
            (await verify.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(3);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptanceOrCancellationWinningAlertLockPreventsWaitingActivation(bool cancel)
    {
        await fixture.ResetAsync();
        var id = await new EscalationActivationTests(fixture).CreateAlert(twoPrimaries: true);
        EscalationClaim claim;
        int version;
        await using (var setup = fixture.CreateContext())
        {
            claim = await Claim(setup, id);
            version = (await setup.Alerts.SingleAsync(x => x.Id == id)).DraftVersion.Value;
        }
        var gate = new PauseAfterSave();
        await using var humanDb = Gated(gate);
        Task human = cancel
            ? new AlertLifecycleService(humanDb).CancelAsync(DemoDataSeeder.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-stop", id,
                new AlertLifecycleActionRequest(version), "SIM-cancel", default)
            : new RecipientResponseService(humanDb, new PractitionerIdentityResolver(humanDb)).RecordAsync(DemoDataSeeder.OrganizationId,
                DemoDataSeeder.RileyUserId, "SIM-accept", id, new RecordRecipientResponseRequest(version, "Accepted"), "SIM-accept", default);
        await gate.Saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var workerDb = fixture.CreateContext();
        await workerDb.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)workerDb.Database.GetDbConnection()).ProcessID;
        var activation = new EscalationRunProcessor(workerDb).ProcessClaimAsync(claim);
        try { await WaitForLock(activation, pid); }
        finally { gate.Release.TrySetResult(); }
        await human;
        (await activation).Should().BeTrue();
        await using var verify = fixture.CreateContext();
        (await verify.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(cancel ? EscalationOutcome.Cancelled : EscalationOutcome.ResponsibilityAccepted);
        (await verify.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(2);
        (await verify.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
    }

    [Fact]
    public async Task PauseInvalidatesOwnedLeaseAndResumeRestoresDelayAcrossRestartWithoutConsumingPendingSignals()
    {
        await fixture.ResetAsync();
        var id = await new EscalationActivationTests(fixture).CreateAlert(twoSteps: true, twoPrimaries: true);
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        var alert = await db.Alerts.SingleAsync(x => x.Id == id);
        foreach (var pair in new[] { (DemoDataSeeder.RileySatoId, RecipientResponseType.Declined), (DemoDataSeeder.MayaChenId, RecipientResponseType.Unavailable) })
            db.RecipientResponses.Add(RecipientResponse.Record(RecipientResponseId.New(), alert.OrganizationId, id, alert.DraftVersion,
                pair.Item1, pair.Item2, DemoDataSeeder.JordanUserId, await new DatabaseClock(db).GetUtcNowAsync(), RecipientResponse.DefaultReasonCode(pair.Item2)));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() + interval '45 seconds' WHERE alert_id = {id.Value}");
        var paused = await new EscalationOverrideService(db).PauseAsync(alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-control", id,
            new(alert.DraftVersion.Value, "OperatorReview"), "SIM-pause", default);
        paused!.State.Should().Be("Paused");
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        var remaining = run.RemainingDelay!.Value;
        remaining.Should().BeGreaterThan(TimeSpan.FromSeconds(30)).And.BeLessThan(TimeSpan.FromSeconds(46));
        run.LeaseOwner.Should().BeNull();
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim)).Should().BeFalse();
        (await db.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
        await using var restart = fixture.CreateContext();
        var resumed = await new EscalationOverrideService(restart).ResumeAsync(alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-control", id,
            new(alert.DraftVersion.Value, "ReadyToResume"), "SIM-resume", default);
        var saved = await restart.EscalationRuns.SingleAsync(x => x.AlertId == id);
        saved.NextDueAtUtc.Should().Be(resumed!.OccurredAtUtc.Add(remaining));
        for (var step = 1; step <= 2; step++)
        {
            await using var worker = fixture.CreateContext();
            var next = await Claim(worker, id, due: false);
            var first = new EscalationRunProcessor(worker).ProcessClaimAsync(next);
            await using var rival = fixture.CreateContext();
            var duplicate = new EscalationRunProcessor(rival).ProcessClaimAsync(next);
            (await Task.WhenAll(first, duplicate)).Should().BeEquivalentTo([true, false]);
        }
        var consumed = await restart.EscalationConsumedSignals.Where(x => x.AlertId == id).OrderBy(x => x.StepSequence).ToArrayAsync();
        consumed.Select(x => x.StepSequence).Should().Equal(1, 2);
        consumed.Select(x => x.ResponseId).Distinct().Should().HaveCount(2);
        (await restart.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(2);
    }

    [Fact]
    public async Task SavedOverrideRollbackLeavesNoEventAuditIdempotencyOrPausedState()
    {
        await fixture.ResetAsync();
        var id = await new EscalationActivationTests(fixture).CreateAlert();
        await using (var setup = fixture.CreateContext()) await new EscalationScheduler(setup).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var gate = new FailAfterSave();
        await using var db = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(gate).Options);
        var version = (await db.Alerts.SingleAsync(x => x.Id == id)).DraftVersion.Value;
        var action = () => new EscalationOverrideService(db).PauseAsync(DemoDataSeeder.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-rollback", id,
            new(version, "OperatorReview"), "SIM-rollback", default);
        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("SIM rollback");
        await using var verify = fixture.CreateContext();
        (await verify.EscalationRuns.SingleAsync(x => x.AlertId == id)).State.Should().Be(EscalationRunState.Scheduled);
        (await verify.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.Paused)).Should().Be(0);
        (await verify.AuditEvents.CountAsync(x => x.ResourceId == id.Value && x.Action == "escalation.paused")).Should().Be(0);
        (await verify.IdempotencyRecords.CountAsync(x => x.OperationType == "escalation-override")).Should().Be(0);
    }

    private async Task<EscalationClaim> Claim(CriticalAlertsDbContext db, AlertId id, bool due = true)
    {
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        if (due) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 second' WHERE id = {run.Id.Value}");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var claim = await new EscalationRunRepository(db).TryClaimAsync(run.Id, "SIM-concurrency", TimeSpan.FromSeconds(30), timeout.Token);
            if (claim is not null) return claim;
            await Task.Delay(20, timeout.Token);
        }
    }

    private CriticalAlertsDbContext Gated(PauseAfterSave gate)
        => new(new DbContextOptionsBuilder<CriticalAlertsDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(gate).Options);

    private async Task WaitForLock(Task task, int pid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var monitor = new NpgsqlConnection(fixture.ConnectionString);
        await monitor.OpenAsync(timeout.Token);
        while (!task.IsCompleted)
        {
            await using var command = new NpgsqlCommand("SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", monitor);
            command.Parameters.AddWithValue("pid", pid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true) return;
            await Task.Delay(10, timeout.Token);
        }
        throw new InvalidOperationException("The competing transaction did not wait for the shared alert lock.");
    }

    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Saved.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("SIM rollback");
    }
}
