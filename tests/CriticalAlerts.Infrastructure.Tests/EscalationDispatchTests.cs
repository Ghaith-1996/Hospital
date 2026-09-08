using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationDispatchTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task FinalStepQueuesOnceAndDispatchesOnlyBackupsAcrossWorkersAndReplay()
    {
        await fixture.ResetAsync();
        var id = await CreateAlert();
        EscalationClaim claim;
        await using (var db = fixture.CreateContext()) claim = await Claim(db, id);
        async Task<bool> Activate()
        {
            await using var db = fixture.CreateContext();
            return await new EscalationRunProcessor(db).ProcessClaimAsync(claim);
        }
        (await Task.WhenAll(Activate(), Activate())).Should().BeEquivalentTo([true, false]);
        async Task<DispatchProcessingResult> Dispatch()
        {
            await using var db = fixture.CreateContext();
            return await Processor(db).ProcessNextAsync("same-process", CancellationToken.None);
        }
        var results = new List<DispatchProcessingResult>();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            results.AddRange(await Task.WhenAll(Dispatch(), Dispatch()));
            if (!results.Any(x => x.Processed)) await Task.Delay(25);
        } while (!results.Any(x => x.Processed) && deadline.Elapsed < TimeSpan.FromSeconds(5));
        results.Should().NotContain(x => x.PermanentlyFailed || x.Rescheduled);
        results.Count(x => x.Processed).Should().Be(1, JsonSerializer.Serialize(results));
        (await Dispatch()).Processed.Should().BeFalse();
        await using var read = fixture.CreateContext();
        var message = await read.OutboxMessages.SingleAsync(x => x.AggregateId == id.Value);
        message.EventType.Should().Be(nameof(EscalationDispatchRequested));
        using var document = JsonDocument.Parse(message.PayloadJson);
        document.RootElement.EnumerateObject().Select(x => x.Name).Should().BeEquivalentTo("alertId", "alertVersion", "escalationRunId", "stepSequence", "recipientSelectionIds");
        (await read.EscalationRuns.SingleAsync(x => x.AlertId == id)).Outcome.Should().Be(EscalationOutcome.Exhausted);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType == EscalationEventType.DispatchQueued)).Should().Be(1);
        var backups = await read.AlertRecipientSelections.Where(x => x.AlertId == id && x.SelectionSource == RecipientSelectionSource.EscalationPolicy).Select(x => x.Id).ToArrayAsync();
        var attempts = await read.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync();
        attempts.Should().HaveCount(2).And.OnlyContain(x => x.Status == DeliveryAttemptStatus.Delivered);
        attempts.Select(x => x.RecipientSelectionId).Should().BeEquivalentTo(backups);
    }
    [Theory]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("duplicate-selection")]
    [InlineData("empty-selection")]
    [InlineData("unknown-selection")]
    [InlineData("manual-selection")]
    [InlineData("wrong-run")]
    [InlineData("wrong-step")]
    [InlineData("wrong-version")]
    [InlineData("wrong-alert")]
    [InlineData("wrong-org")]
    [InlineData("unknown-event")]
    [InlineData("copied-key")]
    public async Task InvalidIdentifierShapeOrMembershipFailsBeforeAnyAdapterCall(string mutation)
    {
        await fixture.ResetAsync();
        var id = await Queue();
        await using var db = fixture.CreateContext();
        var row = await db.OutboxMessages.SingleAsync(x => x.AggregateId == id.Value);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(row.PayloadJson)!.AsObject();
        var type = row.EventType;
        var key = row.IdempotencyKey;
        var organization = row.OrganizationId.Value;
        switch (mutation)
        {
            case "extra": payload["approvedMessage"] = "SIMULATION forbidden"; break;
            case "missing": payload.Remove("stepSequence"); break;
            case "duplicate-selection": payload["recipientSelectionIds"]!.AsArray().Add(payload["recipientSelectionIds"]![0]!.GetValue<string>()); break;
            case "empty-selection": payload["recipientSelectionIds"] = new System.Text.Json.Nodes.JsonArray(); break;
            case "unknown-selection": payload["recipientSelectionIds"]![0] = Guid.NewGuid().ToString(); break;
            case "manual-selection": payload["recipientSelectionIds"]![0] = (await db.AlertRecipientSelections.SingleAsync(x => x.AlertId == id && x.SelectionSource == RecipientSelectionSource.Manual)).Id.Value.ToString(); break;
            case "wrong-run": payload["escalationRunId"] = Guid.NewGuid().ToString(); break;
            case "wrong-step": payload["stepSequence"] = 2; break;
            case "wrong-version": payload["alertVersion"] = 999; break;
            case "wrong-alert": payload["alertId"] = Guid.NewGuid().ToString(); break;
            case "wrong-org":
                var foreign = CriticalAlerts.Domain.Organizations.Organization.CreateSimulation(OrganizationId.New(), "Fictional Other Hospital", DateTimeOffset.UtcNow);
                db.Organizations.Add(foreign);
                await db.SaveChangesAsync();
                organization = foreign.Id.Value;
                break;
            case "unknown-event": type = "UnknownDispatchRequested"; break;
            case "copied-key": key += "-copy"; break;
        }
        var json = payload.ToJsonString();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET payload_json = {json}::jsonb, event_type = {type}, idempotency_key = {key}, organization_id = {organization}, next_attempt_at_utc = clock_timestamp() - interval '1 minute' WHERE id = {row.Id.Value}");
        (await Processor(db).ProcessNextAsync("worker", CancellationToken.None)).PermanentlyFailed.Should().BeTrue();
        (await db.DeliveryAttempts.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await db.Alerts.SingleAsync(x => x.Id == id)).State.Should().Be(AlertState.Active);
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("future-accepted")]
    [InlineData("cancelled")]
    [InlineData("resolved")]
    public async Task QueuedStepIsSuppressedByFreshResponsibilityOrLifecycle(string fact)
    {
        await fixture.ResetAsync();
        var id = await Queue();
        await using var db = fixture.CreateContext();
        if (fact is "accepted" or "future-accepted")
        {
            await AddResponse(db, id, RecipientResponseType.Accepted, true);
            if (fact == "future-accepted")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE responsibility_assignments SET accepted_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {id.Value}");
        }
        else
        {
            var alert = await db.Alerts.Include(x => x.StateTransitions).SingleAsync(x => x.Id == id);
            var now = await new DatabaseClock(db).GetUtcNowAsync();
            if (fact == "cancelled") alert.Cancel(DemoDataSeeder.JordanUserId, now, "dispatch-test");
            else alert.Resolve(DemoDataSeeder.JordanUserId, now, "dispatch-test");
            await db.SaveChangesAsync();
        }
        (await Processor(db).ProcessNextAsync("worker", CancellationToken.None)).Processed.Should().BeTrue();
        (await db.DeliveryAttempts.CountAsync(x => x.AlertId == id)).Should().Be(0);
    }

    [Fact]
    public async Task CapturedRetryBoundFailsVisiblyWithoutDestroyingActiveAlert()
    {
        await fixture.ResetAsync();
        var id = await Queue(); // captured MaxAttempts = 1 despite worker = 3
        await using var db = fixture.CreateContext();
        await new SimulationDispatchScenarioStore(db).SetAsync(DemoDataSeeder.OrganizationId, NotificationChannel.SecureMessage,
            SimulationDispatchScenario.ProviderOutage, DemoDataSeeder.MorganUserId, DateTimeOffset.UtcNow, CancellationToken.None);
        (await Processor(db).ProcessNextAsync("worker", CancellationToken.None)).PermanentlyFailed.Should().BeTrue();
        (await db.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync()).Should().HaveCount(2).And.OnlyContain(x => x.AttemptNumber == 1 && x.Status == DeliveryAttemptStatus.Failed);
        (await db.Alerts.SingleAsync(x => x.Id == id)).State.Should().Be(AlertState.Active);
    }

    [Fact]
    public async Task InitialOutboxAfterBackupActivationStillContainsOnlyOriginalRecipients()
    {
        await fixture.ResetAsync();
        var id = await Queue();
        await using var db = fixture.CreateContext();
        var alert = await db.Alerts.SingleAsync(x => x.Id == id);
        db.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageId.New(), alert.OrganizationId, nameof(AlertDispatchRequested), id.Value,
            JsonSerializer.Serialize(new { alertId = id.Value, draftVersion = alert.DraftVersion.Value }), "original-dispatch", DateTimeOffset.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync();
        (await Processor(db).ProcessNextAsync("initial", CancellationToken.None)).Processed.Should().BeTrue();
        var original = await db.AlertRecipientSelections.SingleAsync(x => x.AlertId == id && x.SelectionSource == RecipientSelectionSource.Manual);
        (await db.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync()).Should().ContainSingle(x => x.RecipientSelectionId == original.Id);
        (await Processor(db).ProcessNextAsync("backup", CancellationToken.None)).Processed.Should().BeTrue();
        (await db.DeliveryAttempts.CountAsync(x => x.AlertId == id)).Should().Be(3);
    }

    [Theory]
    [InlineData(RecipientResponseType.Declined)]
    [InlineData(RecipientResponseType.Unavailable)]
    public async Task SavedActivationAndQueueRollbackTogetherThenRecover(RecipientResponseType responseType)
    {
        await fixture.ResetAsync();
        var id = await CreateAlert();
        EscalationClaim claim;
        await using (var db = fixture.CreateContext())
        {
            await AddResponse(db, id, responseType);
            claim = await Claim(db, id, false);
            var processor = new EscalationRunProcessor(db);
            var execute = () => processor.ProcessClaimAsync(claim, async (step, ct) =>
            {
                await processor.EnqueueStepAsync(step, ct);
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException("SIM failure after saving queue");
            });
            await execute.Should().ThrowAsync<InvalidOperationException>();
        }
        await using var read = fixture.CreateContext();
        (await read.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await read.EscalationConsumedSignals.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await read.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
        (await read.EscalationEvents.CountAsync(x => x.AlertId == id && x.EventType != EscalationEventType.Scheduled)).Should().Be(0);
        await read.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET lease_expires_at_utc = clock_timestamp() - interval '1 second' WHERE id = {claim.RunId.Value}");
        var recovered = (await new EscalationRunRepository(read).TryClaimAsync(claim.RunId, "restart", TimeSpan.FromSeconds(30)))!;
        (await new EscalationRunProcessor(read).ProcessClaimAsync(recovered)).Should().BeTrue();
        (await read.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(1);
    }

    [Fact]
    public async Task RetryAfterRestartHonorsCapturedTwoAttemptMaximum()
    {
        await fixture.ResetAsync();
        var id = await Queue(2);
        await using (var db = fixture.CreateContext())
        {
            await new SimulationDispatchScenarioStore(db).SetAsync(DemoDataSeeder.OrganizationId, NotificationChannel.SecureMessage,
                SimulationDispatchScenario.ProviderOutage, DemoDataSeeder.MorganUserId, DateTimeOffset.UtcNow, CancellationToken.None);
            (await Processor(db).ProcessNextAsync("first", CancellationToken.None)).Rescheduled.Should().BeTrue();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET next_attempt_at_utc = clock_timestamp() - interval '1 second' WHERE aggregate_id = {id.Value}");
        }
        await using var restarted = fixture.CreateContext();
        (await Processor(restarted).ProcessNextAsync("restarted", CancellationToken.None)).PermanentlyFailed.Should().BeTrue();
        var attempts = await restarted.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync();
        attempts.Should().HaveCount(4).And.OnlyContain(x => x.Status == DeliveryAttemptStatus.Failed && x.AttemptNumber <= 2);
        attempts.Select(x => x.IdempotencyKey).Should().OnlyHaveUniqueItems();
        (await restarted.Alerts.SingleAsync(x => x.Id == id)).State.Should().Be(AlertState.Active);
    }

    private async Task<AlertId> Queue(int maxAttempts = 1)
    {
        var id = await CreateAlert(maxAttempts: maxAttempts);
        await using var db = fixture.CreateContext();
        var claim = await Claim(db, id);
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim)).Should().BeTrue();
        return id;
    }

    [Fact]
    public async Task ExceptionAfterSavedFirstRecipientRollsBackBeforeRecordingFailure()
    {
        await fixture.ResetAsync();
        var id = await Queue();
        await using var db = fixture.CreateContext();
        var channel = new HookChannel((number, _) => number == 2 ? Task.FromException(new InvalidOperationException("SIM failure")) : Task.CompletedTask);
        (await Processor(db, channel).ProcessNextAsync("worker", CancellationToken.None)).PermanentlyFailed.Should().BeTrue();
        (await db.DeliveryAttempts.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await db.DeliveryEvents.CountAsync()).Should().Be(0);
        (await db.Alerts.SingleAsync(x => x.Id == id)).State.Should().Be(AlertState.Active);
        (await db.OutboxMessages.SingleAsync(x => x.AggregateId == id.Value)).LastErrorCategory.Should().Be("worker-error");
    }

    [Fact]
    public async Task ExpiredAcquisitionRollsBackSavedAttemptsAndSameOwnerRestartRecoversOnce()
    {
        await fixture.ResetAsync();
        var id = await Queue();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new HookChannel(async (number, ct) =>
        {
            if (number != 2) return; // first recipient has already been saved in the still-open transaction
            entered.SetResult();
            await release.Task.WaitAsync(ct);
        });
        await using var first = fixture.CreateContext();
        var processing = Processor(first, channel, TimeSpan.FromSeconds(1)).ProcessNextAsync("same-owner", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(1200);
        await using (var parallel = fixture.CreateContext())
            (await Processor(parallel).ProcessNextAsync("same-owner", CancellationToken.None)).Processed.Should().BeFalse();
        release.SetResult();
        (await processing).Processed.Should().BeFalse();
        await using var restarted = fixture.CreateContext();
        (await restarted.DeliveryAttempts.CountAsync(x => x.AlertId == id)).Should().Be(0);
        (await Processor(restarted).ProcessNextAsync("same-owner", CancellationToken.None)).Processed.Should().BeTrue();
        (await restarted.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync()).Should().HaveCount(2).And.OnlyContain(x => x.AttemptNumber == 1);
    }

    [Fact]
    public async Task EscalationDueAndLeaseUseDatabaseClockDespiteFutureApplicationClock()
    {
        await fixture.ResetAsync();
        var id = await Queue();
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET next_attempt_at_utc = clock_timestamp() + interval '1 hour' WHERE aggregate_id = {id.Value}");
        var clock = new FixedClock(DateTimeOffset.UtcNow.AddDays(10));
        (await Processor(db, clock: clock).ProcessNextAsync("clock-test", CancellationToken.None)).Processed.Should().BeFalse();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE outbox_messages SET next_attempt_at_utc = clock_timestamp() - interval '1 second' WHERE aggregate_id = {id.Value}");
        (await Processor(db, clock: clock).ProcessNextAsync("clock-test", CancellationToken.None)).Processed.Should().BeTrue();
        (await db.DeliveryAttempts.Where(x => x.AlertId == id).ToArrayAsync()).Should().OnlyContain(x => x.RequestedAtUtc < DateTimeOffset.UtcNow.AddMinutes(1));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task BackwardsClockDefersClaimWithoutMutationUntilDatabaseCatchesUp()
    {
        await fixture.ResetAsync();
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 minute', updated_at_utc = clock_timestamp() + interval '500 milliseconds' WHERE id = {run.Id.Value}");
        var repository = new EscalationRunRepository(db);
        (await repository.FindCandidatesAsync()).Should().NotContain(run.Id);
        (await repository.TryClaimAsync(run.Id, "clock", TimeSpan.FromSeconds(30))).Should().BeNull();
        (await db.EscalationEvents.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await db.EscalationRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id)).LeaseOwner.Should().BeNull();
        await Task.Delay(800);
        (await repository.TryClaimAsync(run.Id, "clock", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    }

    [Fact]
    public async Task FutureCommittedResponsibilityDefersActivationAndNeverQueuesBackup()
    {
        await fixture.ResetAsync();
        var id = await CreateAlert();
        await using var db = fixture.CreateContext();
        await AddResponse(db, id, RecipientResponseType.Accepted, true);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE responsibility_assignments SET accepted_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {id.Value}");
        var claim = await Claim(db, id);
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim)).Should().BeTrue();
        (await db.OutboxMessages.CountAsync(x => x.AggregateId == id.Value)).Should().Be(0);
        (await db.AlertRecipientSelections.CountAsync(x => x.AlertId == id)).Should().Be(1);
        (await db.EscalationRuns.SingleAsync(x => x.AlertId == id)).State.Should().Be(EscalationRunState.Scheduled);
    }

    [Fact]
    public async Task BackwardsClockBeforeExactConfirmationDefersScheduling()
    {
        await fixture.ResetAsync();
        var id = await CreateAlert(confirmedInFuture: true);
        await using var db = fixture.CreateContext();
        (await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id)).Should().BeFalse();
        (await db.EscalationRuns.CountAsync(x => x.AlertId == id)).Should().Be(0);
    }

    private sealed class HookChannel(Func<int, CancellationToken, Task> hook) : INotificationChannel
    {
        private int calls;
        public NotificationChannel ChannelType => NotificationChannel.SecureMessage;
        public string ProviderName => "simulation-secure-message";
        public async Task<NotificationDispatchResult> DispatchAsync(NotificationDispatchRequest request, SimulationDispatchScenario scenario, CancellationToken cancellationToken)
        {
            await hook(Interlocked.Increment(ref calls), cancellationToken);
            return await new SimulationSecureMessageChannel(TimeProvider.System).DispatchAsync(request, scenario, cancellationToken);
        }
    }

    private static OutboxDispatchProcessor Processor(CriticalAlertsDbContext db, INotificationChannel? channel = null,
        TimeSpan? lease = null, TimeProvider? clock = null) => new(db,
        [channel ?? new SimulationSecureMessageChannel(TimeProvider.System), new SimulationSmsChannel(TimeProvider.System), new SimulationVoiceChannel(TimeProvider.System)],
        new SimulationDeliveryEventNormalizer(), new SimulationDispatchScenarioStore(db), clock ?? TimeProvider.System,
        Options.Create(new DispatchWorkerOptions { LeaseDuration = lease ?? TimeSpan.FromSeconds(30), MaxAttempts = 3, RetryDelay = TimeSpan.FromSeconds(1) }),
        NullLogger<OutboxDispatchProcessor>.Instance);
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
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            var claim = await new EscalationRunRepository(db).TryClaimAsync(run.Id, "activation-test", TimeSpan.FromSeconds(30));
            if (claim is not null) return claim;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("Synthetic run did not become claimable within the bounded polling interval.");
    }

    private async Task<AlertId> CreateAlert(bool snapshots = true, bool twoSteps = false, bool twoPrimaries = false, int maxAttempts = 1, bool confirmedInFuture = false)
    {
        await using var db = fixture.CreateContext();
        var now = (await new DatabaseClock(db).GetUtcNowAsync()).AddMinutes(confirmedInFuture ? 5 : -5);
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
        EscalationStepSnapshot[] steps = twoSteps ? [new(1, 60, maxAttempts, "DEMO backup", [recipients[0]]), new(2, 60, maxAttempts, "DEMO backup", [recipients[1]])] : [new(1, 60, maxAttempts, "DEMO backup", recipients)];
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
