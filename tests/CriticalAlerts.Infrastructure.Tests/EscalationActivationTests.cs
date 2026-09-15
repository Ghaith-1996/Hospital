using System.Text;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationActivationTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task ActivatesOnlyConfirmedBackupsWithoutChangingConfirmationOrOriginalSelection()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var before = await db.Alerts.AsNoTracking().SingleAsync(x => x.Id == id);
        var primary = await db.AlertRecipientSelections.SingleAsync(x => x.AlertId == id);
        var claim = await Claim(db, id);
        var calls = 0;
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (step, _) =>
        {
            calls++;
            step.RecipientSelectionIds.Should().HaveCount(2).And.NotContain(primary.Id);
            step.StepSequence.Should().Be(1);
            return Task.CompletedTask;
        })).Should().BeTrue();
        calls.Should().Be(1);
        await using var read = fixture.CreateContext();
        var after = await read.Alerts.SingleAsync(x => x.Id == id);
        after.DraftVersion.Should().Be(before.DraftVersion);
        after.ConfirmedAtUtc.Should().Be(before.ConfirmedAtUtc);
        after.ApprovedMessage!.Ciphertext.Should().Equal(before.ApprovedMessage!.Ciphertext);
        var selections = await read.AlertRecipientSelections.Where(x => x.AlertId == id).ToArrayAsync();
        selections.Should().HaveCount(3);
        selections.Single(x => x.Id == primary.Id).SelectionSource.Should().Be(RecipientSelectionSource.Manual);
        selections.Where(x => x.Id != primary.Id).Should().OnlyContain(x => x.SelectionSource == RecipientSelectionSource.EscalationPolicy && x.SelectedByUserId == DemoDataSeeder.JordanUserId);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.RecipientActivated)).Should().Be(2);
        (await read.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(EscalationOutcome.Exhausted);
        (await read.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("no-channel")]
    [InlineData("inactive-channel")]
    [InlineData("wrong-channel")]
    [InlineData("invalid-label")]
    [InlineData("wrong-role-member")]
    [InlineData("missing-snapshots")]
    public async Task InvalidConfirmedMemberFailsWholeStepWithoutReplacementOrPartialEffects(string invalid)
    {
        var id = await CreateAlert(snapshots: invalid != "missing-snapshots");
        await using var db = fixture.CreateContext();
        if (invalid != "missing-snapshots")
        {
            var snapshot = await db.AlertEscalationRecipientSnapshots.FirstAsync(x => x.AlertId == id);
            if (invalid == "inactive") await db.Practitioners.Where(x => x.Id == snapshot.PractitionerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            if (invalid == "invalid-label")
            {
                var primaryEndpoint = await db.ContactEndpoints.SingleAsync(x => x.PractitionerId == snapshot.PractitionerId);
                db.ContactEndpoints.Add(ContactEndpoint.Create(ContactEndpointId.New(), snapshot.OrganizationId, snapshot.PractitionerId,
                    ContactEndpointKind.SecureMessage, new ProtectedValue(Encoding.UTF8.GetBytes("SIM-ENDPOINT"), "test-v1", "activation-test"),
                    "SIM-SECONDARY", false, "SIM", Guid.NewGuid().ToString()));
                await db.SaveChangesAsync();
                await db.ContactEndpoints.Where(x => x.Id == primaryEndpoint.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SimulationLabel, "invalid label"));
            }
            if (invalid == "no-channel") await db.ContactEndpoints.Where(x => x.PractitionerId == snapshot.PractitionerId).ExecuteDeleteAsync();
            if (invalid == "inactive-channel") await db.ContactEndpoints.Where(x => x.PractitionerId == snapshot.PractitionerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            if (invalid == "wrong-channel") await db.ContactEndpoints.Where(x => x.PractitionerId == snapshot.PractitionerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Kind, ContactEndpointKind.Sms));
            if (invalid == "wrong-role-member") await db.PractitionerRoles.Where(x => x.Id == snapshot.PractitionerRoleId).ExecuteUpdateAsync(s => s.SetProperty(x => x.PractitionerId, DemoDataSeeder.MayaChenId));
        }
        var claim = await Claim(db, id);
        await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (_, _) => throw new InvalidOperationException("Must not queue invalid step"));
        await using var read = fixture.CreateContext();
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.RecipientActivated)).Should().Be(0);
        (await read.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
        var run = await read.EscalationRuns.SingleAsync(x => x.AlertId == id);
        run.Outcome.Should().Be(EscalationOutcome.ProcessingFailed);
        run.FailureCategory.Should().Be(invalid == "missing-snapshots" ? EscalationFailureCategory.ConfirmedPlanInvalid : EscalationFailureCategory.ConfirmedRecipientUnavailable);
        (await read.EscalationEvents.SingleAsync(x => x.RunId == run.Id && x.EventType == EscalationEventType.ProcessingFailed)).FailureCategory.Should().Be(run.FailureCategory);
    }

    [Fact]
    public async Task TwoProcessorsAndClaimReplayActivateExactlyOnce()
    {
        var id = await CreateAlert();
        EscalationClaim claim;
        await using (var setup = fixture.CreateContext()) claim = await Claim(setup, id);
        var queues = 0;
        async Task<bool> Execute()
        {
            await using var db = fixture.CreateContext();
            return await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (_, _) => { Interlocked.Increment(ref queues); return Task.CompletedTask; });
        }
        (await Task.WhenAll(Execute(), Execute())).Should().BeEquivalentTo([true, false]);
        (await Execute()).Should().BeFalse();
        queues.Should().Be(1);
        await using var read = fixture.CreateContext();
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(3);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.StepDue)).Should().Be(1);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.RecipientActivated)).Should().Be(2);
    }

    [Theory]
    [InlineData(RecipientResponseType.Declined)]
    [InlineData(RecipientResponseType.Unavailable)]
    public async Task SignalAndSelectionsRollbackTogetherAndExpiredLeaseRecovers(RecipientResponseType type)
    {
        var id = await CreateAlert(twoSteps: true);
        EscalationClaim failedClaim;
        await using (var db = fixture.CreateContext())
        {
            await AddResponse(db, id, type);
            failedClaim = await Claim(db, id, due: false);
            var execute = () => new EscalationRunProcessor(db).ProcessClaimAsync(failedClaim, async (_, cancellation) =>
            {
                await db.SaveChangesAsync(cancellation);
                throw new InvalidOperationException("SIM rollback after staging all effects");
            });
            await execute.Should().ThrowAsync<InvalidOperationException>();
        }
        await using var read = fixture.CreateContext();
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await read.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType != EscalationEventType.Scheduled)).Should().Be(0);
        await read.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET lease_expires_at_utc = clock_timestamp() - interval '1 second' WHERE id = {failedClaim.RunId.Value}");
        var recovered = (await new EscalationRunRepository(read).TryClaimAsync(failedClaim.RunId, "restart", TimeSpan.FromSeconds(30)))!;
        (await new EscalationRunProcessor(read).ProcessClaimAsync(failedClaim, (_, _) => throw new InvalidOperationException("stale claim"))).Should().BeFalse();
        await new EscalationRunProcessor(read).ProcessClaimAsync(recovered, (_, _) => Task.CompletedTask);
        (await read.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(2);
        var next = await new EscalationRunRepository(read).TryClaimAsync(failedClaim.RunId, "later", TimeSpan.FromSeconds(30));
        next.Should().BeNull("one response cannot accelerate a second step");
    }

    [Theory]
    [InlineData("resolved", EscalationOutcome.Resolved)]
    [InlineData("cancelled", EscalationOutcome.Cancelled)]
    [InlineData("responsibility", EscalationOutcome.ResponsibilityAccepted)]
    public async Task FreshStopFactsWinOverDueSignalAndInvalidBackup(string fact, EscalationOutcome outcome)
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        if (fact != "responsibility") await AddResponse(db, id, RecipientResponseType.Declined);
        await using (var update = fixture.CreateContext())
        {
            var alert = await update.Alerts.SingleAsync(x => x.Id == id);
            var now = await new DatabaseClock(update).GetUtcNowAsync();
            if (fact == "resolved") alert.Resolve(DemoDataSeeder.JordanUserId, now, "activation-test");
            if (fact == "cancelled") alert.Cancel(DemoDataSeeder.JordanUserId, now, "activation-test");
            if (fact == "responsibility") await AddResponse(update, id, RecipientResponseType.Accepted, assign: true);
            var backup = await update.AlertEscalationRecipientSnapshots.FirstAsync(x => x.AlertId == id);
            await update.Practitioners.Where(x => x.Id == backup.PractitionerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            await update.SaveChangesAsync();
        }
        await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (_, _) => throw new InvalidOperationException("Stop must win"));
        await using var read = fixture.CreateContext();
        (await read.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(outcome);
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await read.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.StepDue)).Should().Be(0);
    }

    [Fact]
    public async Task PauseInvalidatesClaimAndRetainsSignalsUntilResume()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        await AddResponse(db, id, RecipientResponseType.Unavailable);
        await using (var update = fixture.CreateContext())
        {
            var run = await update.EscalationRuns.SingleAsync(x => x.AlertId == id);
            run.Pause(DemoDataSeeder.JordanUserId, await new DatabaseClock(update).GetUtcNowAsync());
            await update.SaveChangesAsync();
        }
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (_, _) => throw new InvalidOperationException("Paused"))).Should().BeFalse();
        (await db.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
        await using var resume = fixture.CreateContext();
        var resumed = await resume.EscalationRuns.SingleAsync(x => x.AlertId == id);
        resumed.Resume(DemoDataSeeder.JordanUserId, await new DatabaseClock(resume).GetUtcNowAsync());
        await resume.SaveChangesAsync();
        var resumedClaim = (await new EscalationRunRepository(resume).TryClaimAsync(resumed.Id, "resume", TimeSpan.FromSeconds(30)))!;
        await new EscalationRunProcessor(resume).ProcessClaimAsync(resumedClaim, (_, _) => Task.CompletedTask);
        (await resume.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(1);
    }

    [Theory]
    [InlineData(RecipientResponseType.Acknowledged)]
    [InlineData(RecipientResponseType.CallUnitRequested)]
    [InlineData(RecipientResponseType.Accepted)]
    public async Task NonResponsibilityResponsesNeitherStopNorAccelerate(RecipientResponseType type)
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await AddResponse(db, id, type);
        (await Claim(db, id, due: false)).Should().BeNull();
        var due = await Claim(db, id);
        await new EscalationRunProcessor(db).ProcessClaimAsync(due, (_, _) => Task.CompletedTask);
        (await db.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(3);
        (await db.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
    }

    [Fact]
    public async Task MultipleActiveResponsibilitiesStopWithoutProcessingFailure()
    {
        var id = await CreateAlert(twoPrimaries: true);
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        await AddResponse(db, id, RecipientResponseType.Accepted, assign: true, practitioner: DemoDataSeeder.MayaChenId);
        await AddResponse(db, id, RecipientResponseType.Accepted, assign: true, practitioner: DemoDataSeeder.RileySatoId);
        await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (_, _) => throw new InvalidOperationException("Already accepted"));
        await using var read = fixture.CreateContext();
        (await read.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(EscalationOutcome.ResponsibilityAccepted);
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(2);
    }

    [Fact]
    public async Task CurrentDirectoryValidationRemainsStableUntilActivationCommit()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        var backup = await db.AlertEscalationRecipientSnapshots.FirstAsync(x => x.AlertId == id);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = new EscalationRunProcessor(db).ProcessClaimAsync(claim, async (_, _) => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var update = fixture.CreateContext();
        var deactivation = update.Practitioners.Where(x => x.Id == backup.PractitionerId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        try
        {
            await Task.Delay(150);
            deactivation.IsCompleted.Should().BeFalse("directory writes must wait for activation commit");
        }
        finally { release.TrySetResult(); }
        (await operation).Should().BeTrue();
        await deactivation;
        await using var read = fixture.CreateContext();
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(3);
        (await read.Practitioners.SingleAsync(x => x.Id == backup.PractitionerId)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task PendingSignalsAreConsumedInDurableOrderOnePerStep()
    {
        var id = await CreateAlert(twoSteps: true, twoPrimaries: true);
        await using var db = fixture.CreateContext();
        await AddResponse(db, id, RecipientResponseType.Unavailable, practitioner: DemoDataSeeder.RileySatoId);
        await AddResponse(db, id, RecipientResponseType.Declined, practitioner: DemoDataSeeder.MayaChenId);
        var responses = await db.RecipientResponses.Where(x => x.AlertId == id).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).ToArrayAsync();
        var first = await Claim(db, id, due: false);
        await new EscalationRunProcessor(db).ProcessClaimAsync(first, (_, _) => Task.CompletedTask);
        (await db.EscalationConsumedSignals.SingleAsync(x => x.AlertId == id)).ResponseId.Should().Be(responses[0].Id);
        await using var restart = fixture.CreateContext();
        var second = (await new EscalationRunRepository(restart).TryClaimAsync(first.RunId, "second-step", TimeSpan.FromSeconds(30)))!;
        await new EscalationRunProcessor(restart).ProcessClaimAsync(second, (_, _) => Task.CompletedTask);
        var consumed = await restart.EscalationConsumedSignals.Where(x => x.AlertId == id).OrderBy(x => x.StepSequence).ToArrayAsync();
        consumed.Select(x => x.ResponseId).Should().Equal(responses.Select(x => x.Id));
        consumed.Select(x => x.StepSequence).Should().Equal(1, 2);
        (await restart.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(4);
    }

    [Theory]
    [InlineData("missing-practitioner")]
    [InlineData("missing-role")]
    [InlineData("foreign-role")]
    public async Task ScopedSnapshotForeignKeysRejectRemovingOrForeignizingConfirmedIdentity(string mutation)
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var snapshot = await db.AlertEscalationRecipientSnapshots.FirstAsync(x => x.AlertId == id);
        Func<Task> alter = mutation switch
        {
            "missing-practitioner" => async () => await db.Practitioners.Where(x => x.Id == snapshot.PractitionerId).ExecuteDeleteAsync(),
            "missing-role" => async () => await db.PractitionerRoles.Where(x => x.Id == snapshot.PractitionerRoleId).ExecuteDeleteAsync(),
            _ => async () => await db.PractitionerRoles.Where(x => x.Id == snapshot.PractitionerRoleId).ExecuteUpdateAsync(s => s.SetProperty(x => x.OrganizationId, new OrganizationId(Guid.NewGuid()))),
        };
        await alter.Should().ThrowAsync<Npgsql.PostgresException>().Where(x => x.SqlState == (mutation == "foreign-role" ? "23503" : "23001"));
        (await db.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await db.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
        (await db.AlertEscalationRecipientSnapshots.SingleAsync(x => x.Id == snapshot.Id)).PractitionerRoleId.Should().Be(snapshot.PractitionerRoleId);
    }

    [Fact]
    public async Task ActivationUsesConfirmedPlanAfterMutablePolicyEdit()
    {
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        var plan = await db.AlertEscalationPlans.SingleAsync(x => x.AlertId == id);
        var policy = await db.EscalationPolicies.AsNoTracking().SingleAsync(x => x.Id == plan.EscalationPolicyId);
        await db.EscalationPolicies.Where(x => x.Id == policy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TriggerCondition, "SIM later unsupported rule"));
        try
        {
            await new EscalationRunProcessor(db).ProcessClaimAsync(claim, (step, _) =>
            {
                step.MaxAttempts.Should().Be(plan.Definition.Steps[0].MaxAttempts);
                return Task.CompletedTask;
            });
            var actual = await db.AlertRecipientSelections.Where(x => x.AlertId == id && x.SelectionSource == RecipientSelectionSource.EscalationPolicy).Select(x => x.PractitionerId).ToArrayAsync();
            actual.Select(x => x.Value).Should().BeEquivalentTo(plan.Definition.Steps[0].Recipients.Select(x => x.PractitionerId));
        }
        finally { await db.EscalationPolicies.Where(x => x.Id == policy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.TriggerCondition, policy.TriggerCondition)); }
    }

    private static async Task AddResponse(CriticalAlertsDbContext db, AlertId id, RecipientResponseType type, bool assign = false, PractitionerId? practitioner = null)
    {
        var selection = await db.AlertRecipientSelections.FirstAsync(x => x.AlertId == id && (practitioner == null || x.PractitionerId == practitioner));
        var response = RecipientResponse.Record(RecipientResponseId.New(), selection.OrganizationId, id, selection.AlertVersion,
            selection.PractitionerId, type, DemoDataSeeder.JordanUserId, await new DatabaseClock(db).GetUtcNowAsync(), RecipientResponse.DefaultReasonCode(type));
        db.RecipientResponses.Add(response);
        if (assign) db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
        await db.SaveChangesAsync();
    }

    private async Task<EscalationClaim> Claim(CriticalAlertsDbContext db, AlertId id, bool due = true)
    {
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        if (due) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 second' WHERE id = {run.Id.Value}");
        return (await new EscalationRunRepository(db).TryClaimAsync(run.Id, "activation-test", TimeSpan.FromSeconds(30)))!;
    }

    internal async Task<AlertId> CreateAlert(bool snapshots = true, bool twoSteps = false, bool twoPrimaries = false)
    {
        await using var db = fixture.CreateContext();
        var now = (await new DatabaseClock(db).GetUtcNowAsync()).AddMinutes(-5);
        static ProtectedValue Protect(string value, string purpose = "activation-test") => new(Encoding.UTF8.GetBytes(value), "test-v1", purpose);
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId, "SIM-PAT-ACTIVATION", Protect("SIM-PAT-ACTIVATION", ProtectedValuePurposes.AlertPatientReference),
            "SIMULATION room", "DEMO-URGENT", AlertSourceType.Typed, Protect("SIMULATION source"), now, Protect("{\"situation\":\"SIMULATION activation\"}"));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var primary = await db.Practitioners.SingleAsync(x => x.Id == DemoDataSeeder.MayaChenId);
        var primaryRole = await db.PractitionerRoles.FirstAsync(x => x.PractitionerId == primary.Id);
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.SetApprovedMessage(Protect("SIMULATION message"), alert.DraftVersion, now);
        var primaries = new List<Practitioner> { primary };
        var selected = new List<ValidatedRecipientSelection> { new(primary.Id, primaryRole.Id, NotificationChannel.SecureMessage, "DEMO-revision", now, "No assignment") };
        if (twoPrimaries)
        {
            var other = await db.Practitioners.SingleAsync(x => x.Id == DemoDataSeeder.RileySatoId);
            var otherRole = await db.PractitionerRoles.FirstAsync(x => x.PractitionerId == other.Id);
            primaries.Add(other);
            selected.Add(new(other.Id, otherRole.Id, NotificationChannel.SecureMessage, "DEMO-revision", now, "No assignment"));
        }
        alert.ReplaceRecipients(selected, DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion, primaries, now, "activation-test");
        var recipients = new List<EscalationRecipientEvidence>();
        for (var index = 0; index < 2; index++)
        {
            var practitioner = Practitioner.Create(PractitionerId.New(), alert.OrganizationId, "Fictional", "Backup", $"SIM-ACT-{Guid.NewGuid():N}", "DEMO", true, now);
            var role = PractitionerRoleAssignment.Create(PractitionerRoleId.New(), alert.OrganizationId, practitioner.Id, DemoDataSeeder.EmergencyDepartmentId, "DEMO backup", true, "SIM", Guid.NewGuid().ToString());
            db.Practitioners.Add(practitioner);
            db.PractitionerRoles.Add(role);
            db.ContactEndpoints.Add(ContactEndpoint.Create(ContactEndpointId.New(), alert.OrganizationId, practitioner.Id, ContactEndpointKind.SecureMessage, Protect("SIM-ENDPOINT"), "SIM-ENDPOINT", true, "SIM", Guid.NewGuid().ToString()));
            recipients.Add(new(practitioner.Id.Value, role.Id.Value, "Fictional Backup", "DEMO backup", "SecureMessage", "DEMO-revision", now, "No assignment"));
        }
        var policy = await db.EscalationPolicies.FirstAsync(x => x.OrganizationId == alert.OrganizationId);
        EscalationStepSnapshot[] steps = twoSteps ? [new(1, 60, 1, "DEMO backup", [recipients[0]]), new(2, 60, 1, "DEMO backup", [recipients[1]])] : [new(1, 60, 1, "DEMO backup", recipients)];
        var definition = new EscalationPlanDefinition(alert.OrganizationId.Value, alert.Id.Value, alert.DraftVersion.Value, policy.Id.Value, policy.Version,
            DemoEscalationSemantics.TriggerCondition, DemoEscalationSemantics.StopCondition, "DEMO-primary", steps);
        var plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), DemoDataSeeder.JordanUserId, now);
        alert.BindExactEscalationPlan(plan);
        db.AlertEscalationPlans.Add(plan);
        if (snapshots) db.AlertEscalationRecipientSnapshots.AddRange(AlertEscalationRecipientSnapshot.FromPlan(plan));
        alert.MarkActive(now, "activation-test");
        await db.SaveChangesAsync();
        return alert.Id;
    }
}
