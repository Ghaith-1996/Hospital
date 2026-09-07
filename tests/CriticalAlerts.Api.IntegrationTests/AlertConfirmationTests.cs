using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertConfirmationTests(SeededPostgresApiFixture fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EvidenceWritesAreSerializedThroughConfirmationCommit(bool writerFirst, bool directory)
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        await using var writer = fixture.CreateContext();
        var connectionString = writer.Database.GetConnectionString()!;
        var gate = new PauseAfterSnapshotSave();
        await using var confirmationDb = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(gate).Options);
        await confirmationDb.Database.OpenConnectionAsync();
        await writer.Database.OpenConnectionAsync();
        var confirmationPid = ((NpgsqlConnection)confirmationDb.Database.GetDbConnection()).ProcessID;
        var writerPid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
        var service = new AlertReviewService(confirmationDb, AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey), TimeProvider.System);
        var writeSql = directory
            ? "UPDATE practitioners SET last_name = last_name || ' SIM-lock' WHERE organization_id = {0} AND simulation_code = 'SIM-PRAC-0103'"
            : "UPDATE escalation_steps SET max_attempts = max_attempts + 1 WHERE organization_id = {0}";
        var restoreSql = directory
            ? "UPDATE practitioners SET last_name = replace(last_name, ' SIM-lock', '') WHERE organization_id = {0} AND simulation_code = 'SIM-PRAC-0103'"
            : "UPDATE escalation_steps SET max_attempts = max_attempts - 1 WHERE organization_id = {0}";
        Task<ConfirmAlertReviewResult?> Confirm() => service.ConfirmAsync(DemoDataSeeder.OrganizationId, DemoDataSeeder.JordanUserId,
            "SIM-snapshot-lock", new AlertId(prepared.AlertId), new(prepared.Version, reviewedRevisions[prepared.AlertId]),
            $"snapshot-lock-{Guid.NewGuid():N}", default);
        if (writerFirst)
        {
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await writer.Database.ExecuteSqlRawAsync(writeSql, DemoDataSeeder.OrganizationId.Value);
            var confirming = Confirm();
            try { await WaitForLockAsync(confirming, confirmationPid, connectionString); }
            finally { await transaction.CommitAsync(); }
            try
            {
                var act = async () => await confirming;
                await act.Should().ThrowAsync<AlertReviewValidationException>().Where(e => e.Code == "escalation-plan-changed");
                (await writer.AlertEscalationPlans.CountAsync(p => p.AlertId == new AlertId(prepared.AlertId))).Should().Be(0);
                (await writer.OutboxMessages.CountAsync(m => m.AggregateId == prepared.AlertId)).Should().Be(0);
            }
            finally { await writer.Database.ExecuteSqlRawAsync(restoreSql, DemoDataSeeder.OrganizationId.Value); }
        }
        else
        {
            var confirming = Confirm();
            await gate.Saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var writing = writer.Database.ExecuteSqlRawAsync(writeSql, DemoDataSeeder.OrganizationId.Value);
            try { await WaitForLockAsync(writing, writerPid, connectionString); }
            finally { gate.Release.TrySetResult(); }
            (await confirming)!.State.Should().Be("DispatchQueued");
            await writing;
            try
            {
                var plan = await writer.AlertEscalationPlans.SingleAsync(p => p.AlertId == new AlertId(prepared.AlertId));
                plan.Revision.Should().Be(reviewedRevisions[prepared.AlertId]);
                AlertEscalationPlan.ComputeRevision(plan.Definition).Should().Be(plan.Revision);
            }
            finally { await writer.Database.ExecuteSqlRawAsync(restoreSql, DemoDataSeeder.OrganizationId.Value); }
        }
    }

    private static async Task WaitForLockAsync(Task task, int pid, string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var monitor = new NpgsqlConnection(connectionString);
        await monitor.OpenAsync(timeout.Token);
        while (!task.IsCompleted)
        {
            await using var command = new NpgsqlCommand("SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", monitor);
            command.Parameters.AddWithValue("pid", pid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true) return;
            await Task.Delay(10, timeout.Token);
        }
        task.IsCompleted.Should().BeFalse("the competing transaction must wait on PostgreSQL evidence locks");
    }

    private sealed class PauseAfterSnapshotSave : SaveChangesInterceptor
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

    [Theory]
    [InlineData("policy")]
    [InlineData("step")]
    [InlineData("directory")]
    [InlineData("on-call")]
    public async Task ChangedPolicyStepDirectoryOrOnCallEvidenceInvalidatesReview(string kind)
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        await using var db = fixture.CreateContext();
        await using var change = await db.Database.BeginTransactionAsync();
        var sql = kind switch
        {
            "policy" => "UPDATE escalation_policies SET stop_condition = stop_condition || ' SIM-changed' WHERE organization_id = {0}",
            "step" => "UPDATE escalation_steps SET max_attempts = max_attempts + 1 WHERE organization_id = {0}",
            "directory" => "UPDATE practitioners SET last_name = last_name || ' SIM-changed' WHERE organization_id = {0} AND simulation_code = 'SIM-PRAC-0103'",
            _ => "UPDATE on_call_assignments SET last_synchronized_at_utc = last_synchronized_at_utc + interval '1 second' WHERE organization_id = {0}",
        };
        // Commit the change so a fresh confirmation transaction must see it; restore exact values below.
        (await db.Database.ExecuteSqlRawAsync(sql, DemoDataSeeder.OrganizationId.Value)).Should().BeGreaterThan(0);
        await change.CommitAsync();
        try
        {
            using var response = await ConfirmAsync(client, prepared.AlertId, prepared.Version, $"changed-{Guid.NewGuid():N}");
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain("escalation-plan-changed");
            (await db.AlertEscalationPlans.CountAsync(p => p.AlertId == new AlertId(prepared.AlertId))).Should().Be(0);
            (await db.OutboxMessages.CountAsync(m => m.AggregateId == prepared.AlertId)).Should().Be(0);
        }
        finally
        {
            var restore = kind switch
            {
                "policy" => "UPDATE escalation_policies SET stop_condition = replace(stop_condition, ' SIM-changed', '') WHERE organization_id = {0}",
                "step" => "UPDATE escalation_steps SET max_attempts = max_attempts - 1 WHERE organization_id = {0}",
                "directory" => "UPDATE practitioners SET last_name = replace(last_name, ' SIM-changed', '') WHERE organization_id = {0} AND simulation_code = 'SIM-PRAC-0103'",
                _ => "UPDATE on_call_assignments SET last_synchronized_at_utc = last_synchronized_at_utc - interval '1 second' WHERE organization_id = {0}",
            };
            await db.Database.ExecuteSqlRawAsync(restore, DemoDataSeeder.OrganizationId.Value);
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("foreign-role")]
    [InlineData("primary-overlap")]
    [InlineData("cross-step-duplicate")]
    public async Task InvalidBackupPlanIsRejectedBeforeConfirmationWithoutReplacement(string kind)
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        await using var db = fixture.CreateContext();
        var policy = await db.EscalationPolicies.SingleAsync(p => p.OrganizationId == DemoDataSeeder.OrganizationId);
        var step = await db.EscalationSteps.SingleAsync(s => s.PolicyId == policy.Id);
        var originalSource = step.RecipientSource;
        var backupRole = new PractitionerRoleId(Guid.Parse(originalSource["DEMO-role:".Length..]));
        var backupId = (await db.PractitionerRoles.SingleAsync(r => r.Id == backupRole)).PractitionerId;
        var extraId = EscalationStepId.New();
        switch (kind)
        {
            case "inactive":
                await db.Practitioners.Where(p => p.Id == backupId).ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, false));
                break;
            case "cross-step-duplicate":
                db.EscalationSteps.Add(EscalationStep.CreateDemo(extraId, policy.OrganizationId, policy.Id, 2, backupRole));
                await db.SaveChangesAsync();
                break;
            case "foreign-role":
                var foreign = await fixture.CreateForeignOperatorDraftAsync();
                var foreignAlert = await db.Alerts.SingleAsync(a => a.Id == new AlertId(foreign.AlertId));
                var person = Practitioner.Create(PractitionerId.New(), foreignAlert.OrganizationId, "Fictional", "Foreign Backup",
                    $"SIM-{Guid.NewGuid():N}", "DEMO", true, DateTimeOffset.UtcNow);
                var foreignRole = PractitionerRoleAssignment.Create(PractitionerRoleId.New(), foreignAlert.OrganizationId,
                    person.Id, foreignAlert.DepartmentId, "DEMO foreign role", true, "SIM-DIRECTORY", $"SIM-SRC-{Guid.NewGuid():N}");
                db.Practitioners.Add(person);
                db.PractitionerRoles.Add(foreignRole);
                await db.SaveChangesAsync();
                await db.EscalationSteps.Where(s => s.Id == step.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientSource, $"DEMO-role:{foreignRole.Id.Value:D}"));
                break;
            default:
                var id = (await db.PractitionerRoles.SingleAsync(r => r.PractitionerId == DemoDataSeeder.MayaChenId
                    && r.SourceSystem == "SIM-DIRECTORY" && r.SourceRecordId == "SIM-SRC-MAYA")).Id.Value;
                await db.EscalationSteps.Where(s => s.Id == step.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientSource, $"DEMO-role:{id:D}"));
                break;
        }
        try
        {
            using var review = await client.GetAsync($"/api/v1/alerts/{prepared.AlertId:D}/review");
            review.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await review.Content.ReadAsStringAsync()).Should().Contain("escalation-plan-invalid");
            using var confirm = await ConfirmAsync(client, prepared.AlertId, prepared.Version, $"invalid-{Guid.NewGuid():N}");
            confirm.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await db.OutboxMessages.CountAsync(m => m.AggregateId == prepared.AlertId)).Should().Be(0);
        }
        finally
        {
            await db.Practitioners.Where(p => p.Id == backupId).ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, true));
            await db.EscalationSteps.Where(s => s.Id == step.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientSource, originalSource));
            await db.EscalationSteps.Where(s => s.Id == extraId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task ConfirmedSnapshotsStayImmutableAndReplaySurvivesLaterPolicyChanges()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        var key = $"immutable-{Guid.NewGuid():N}";
        using var first = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var db = fixture.CreateContext();
        var plan = await db.AlertEscalationPlans.SingleAsync(p => p.AlertId == new AlertId(prepared.AlertId));
        var originalRevision = plan.Revision;
        var backup = await db.AlertEscalationRecipientSnapshots.SingleAsync(p => p.AlertId == plan.AlertId);
        var alert = await db.Alerts.Include(a => a.RecipientSelections).SingleAsync(a => a.Id == plan.AlertId);
        alert.AutomaticEscalationEligible.Should().BeTrue();
        alert.ExactEscalationPlanRevision.Should().Be(reviewedRevisions[prepared.AlertId]);
        alert.CurrentRecipients.Should().NotContain(r => r.PractitionerId == backup.PractitionerId);
        plan.Definition.Steps.Single().MaxAttempts.Should().Be(1);
        backup.ConfirmedByUserId.Should().Be(DemoDataSeeder.JordanUserId);
        backup.PlanRevision.Should().Be(plan.Revision);
        backup.AlertVersion.Should().Be(prepared.Version);
        var sqlMutation = async () => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_escalation_plans SET revision = 'changed' WHERE id = {plan.Id.Value}");
        await sqlMutation.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == "23514");
        var sqlDelete = async () => await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM alert_escalation_recipient_snapshots WHERE id = {backup.Id.Value}");
        await sqlDelete.Should().ThrowAsync<Npgsql.PostgresException>().Where(e => e.SqlState == "23514");
        await using (var truncateDb = fixture.CreateContext())
        await using (var truncateTransaction = await truncateDb.Database.BeginTransactionAsync())
        {
            var truncate = async () => await truncateDb.Database.ExecuteSqlRawAsync("TRUNCATE alert_escalation_recipient_snapshots");
            await truncate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
        }
        db.Entry(plan).Property(p => p.Revision).CurrentValue = "changed";
        var efMutation = async () => await db.SaveChangesAsync();
        await efMutation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable*");
        db.ChangeTracker.Clear();
        await db.EscalationPolicies.Where(p => p.Id == plan.EscalationPolicyId).ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, false));
        try
        {
            using var replay = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key);
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
            (await replay.Content.ReadFromJsonAsync<ConfirmAlertReviewResult>())!.Replayed.Should().BeTrue();
            reviewedRevisions[prepared.AlertId] = "different-revision";
            using var conflict = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await conflict.Content.ReadAsStringAsync()).Should().Contain("idempotency-conflict");
            (await db.AlertEscalationPlans.SingleAsync(p => p.Id == plan.Id)).Revision.Should().Be(originalRevision);
        }
        finally
        {
            await db.EscalationPolicies.Where(p => p.Id == plan.EscalationPolicyId).ExecuteUpdateAsync(p => p.SetProperty(x => x.IsActive, true));
        }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest)]
    [InlineData("wrong-revision", HttpStatusCode.Conflict)]
    public async Task ExactEscalationRevisionIsRequiredAndMismatchHasNoSideEffects(string? revision, HttpStatusCode expected)
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{prepared.AlertId:D}/confirm")
        {
            Content = JsonContent.Create(new { expectedVersion = prepared.Version, expectedEscalationPlanRevision = revision }),
        };
        request.Headers.Add("Idempotency-Key", $"exact-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(expected);
        await using var db = fixture.CreateContext();
        (await db.Alerts.SingleAsync(a => a.Id == new AlertId(prepared.AlertId))).State.Should().Be(AlertState.PendingConfirmation);
        (await db.OutboxMessages.CountAsync(m => m.AggregateId == prepared.AlertId)).Should().Be(0);
        (await db.AuditEvents.CountAsync(a => a.ResourceId == prepared.AlertId && a.Action == "alert.confirmed")).Should().Be(0);
    }

    [Fact]
    public async Task ReviewExposesExactFutureBackupAndStableRevision()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        using var first = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/alerts/{prepared.AlertId:D}/review"));
        using var second = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/alerts/{prepared.AlertId:D}/review"));
        first.RootElement.TryGetProperty("escalationPlan", out var plan).Should().BeTrue();
        plan.GetProperty("revision").GetString().Should().NotBeNullOrWhiteSpace();
        plan.GetProperty("policyId").GetGuid().Should().NotBeEmpty();
        plan.GetProperty("policyVersion").GetString().Should().Be("DEMO-1");
        plan.GetRawText().Should().Be(second.RootElement.GetProperty("escalationPlan").GetRawText());
        plan.GetProperty("steps")[0].GetProperty("recipients")[0].GetProperty("displayName").GetString().Should().Be("Jules Martin");
        plan.GetProperty("steps")[0].GetProperty("recipients")[0].GetProperty("channel").GetString().Should().Be("SecureMessage");
        plan.GetProperty("steps")[0].GetProperty("recipients")[0].GetProperty("directorySourceUpdatedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task ConfirmationRequiresAKeyCreatesOneIdentifierOnlyOutboxAndReplaysSafely()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);

        using var missingKey = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key: null);
        var missingKeyBody = await missingKey.Content.ReadAsStringAsync();

        using var invalidKey = await ConfirmAsync(client, prepared.AlertId, prepared.Version, new string('x', 129));
        var invalidKeyBody = await invalidKey.Content.ReadAsStringAsync();

        using var first = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-replay");
        var firstResult = await first.Content.ReadFromJsonAsync<ConfirmAlertReviewResult>();
        using var replay = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-replay");
        var replayResult = await replay.Content.ReadFromJsonAsync<ConfirmAlertReviewResult>();

        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingKeyBody.Should().Contain("idempotency-key-required");
        invalidKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidKeyBody.Should().Contain("idempotency-key-invalid");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResult.Should().Be(new ConfirmAlertReviewResult(
            prepared.AlertId,
            prepared.Version,
            "DispatchQueued",
            false));
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResult.Should().Be(new ConfirmAlertReviewResult(
            prepared.AlertId,
            prepared.Version,
            "DispatchQueued",
            true));

        await using var db = fixture.CreateContext();
        var alert = await db.Alerts
            .Include(candidate => candidate.StateTransitions)
            .SingleAsync(candidate => candidate.Id == new AlertId(prepared.AlertId));
        var outbox = await db.OutboxMessages.SingleAsync(message => message.AggregateId == prepared.AlertId);
        var audit = await db.AuditEvents
            .Where(entry => entry.ResourceId == prepared.AlertId && entry.Action == "alert.confirmed")
            .ToArrayAsync();
        var idempotency = await db.IdempotencyRecords
            .Where(record => record.OrganizationId == DemoDataSeeder.OrganizationId
                && record.OperationType == "confirm-review"
                && record.IdempotencyKey == "phase6-confirm-replay")
            .ToArrayAsync();
        using var payload = JsonDocument.Parse(outbox.PayloadJson);

        alert.State.Should().Be(AlertState.DispatchQueued);
        alert.StateTransitions.Count(transition => transition.ToState == AlertState.DispatchQueued).Should().Be(1);
        audit.Should().ContainSingle();
        idempotency.Should().ContainSingle(record => record.Status == IdempotencyProcessingStatus.Completed);
        outbox.EventType.Should().Be("AlertDispatchRequested");
        outbox.ProcessingState.Should().Be(OutboxProcessingState.Pending);
        outbox.IdempotencyKey.Should().Be($"alert-dispatch:{prepared.AlertId:D}:v{prepared.Version}");
        payload.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("alertId", "draftVersion");
        payload.RootElement.GetProperty("alertId").GetGuid().Should().Be(prepared.AlertId);
        payload.RootElement.GetProperty("draftVersion").GetInt32().Should().Be(prepared.Version);
        audit[0].SanitizedMetadata.Should().Contain("recipientCount");
        audit[0].SanitizedMetadata.Should().Contain("SecureMessage");
        (await db.DeliveryAttempts.CountAsync(attempt => attempt.AlertId == new AlertId(prepared.AlertId))).Should().Be(0);
    }

    [Fact]
    public async Task SameKeyWithDifferentVersionAndStaleConfirmationReturnSafeConflicts()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);

        using var first = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-conflict");
        using var differentVersion = await ConfirmAsync(client, prepared.AlertId, prepared.Version + 1, "phase6-confirm-conflict");
        using var stale = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-stale");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        differentVersion.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await differentVersion.Content.ReadAsStringAsync()).Should().Contain("idempotency-conflict");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().NotContain("SIMULATION:");
    }

    [Fact]
    public async Task ConcurrentSameKeyConfirmationProducesOneDurableResult()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);

        var responses = await Task.WhenAll(
            ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-concurrent"),
            ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-concurrent"));
        var statuses = responses.Select(response => response.StatusCode).ToArray();
        var bodies = new List<ConfirmAlertReviewResult>();
        var nonOkBodies = new List<string>();
        foreach (var response in responses)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                bodies.Add((await response.Content.ReadFromJsonAsync<ConfirmAlertReviewResult>())!);
            }
            else
            {
                nonOkBodies.Add(await response.Content.ReadAsStringAsync());
            }

            response.Dispose();
        }

        statuses.Should().OnlyContain(
            status => status == HttpStatusCode.OK,
            "non-OK response bodies were: {0}",
            string.Join(" | ", nonOkBodies));
        bodies.Should().HaveCount(2);
        bodies.Should().ContainSingle(result => !result.Replayed);
        bodies.Should().ContainSingle(result => result.Replayed);

        await using var db = fixture.CreateContext();
        (await db.OutboxMessages.CountAsync(message => message.AggregateId == prepared.AlertId)).Should().Be(1);
        (await db.AuditEvents.CountAsync(entry => entry.ResourceId == prepared.AlertId && entry.Action == "alert.confirmed"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ConfirmationRollsBackWhenTheStableOutboxKeyAlreadyExists()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        var stableOutboxKey = $"alert-dispatch:{prepared.AlertId:D}:v{prepared.Version}";

        await using (var seed = fixture.CreateContext())
        {
            seed.OutboxMessages.Add(OutboxMessage.Create(
                OutboxMessageId.New(),
                DemoDataSeeder.OrganizationId,
                "AlertDispatchRequested",
                prepared.AlertId,
                JsonSerializer.Serialize(new { alertId = prepared.AlertId, draftVersion = prepared.Version }),
                stableOutboxKey,
                DateTimeOffset.UtcNow));
            await seed.SaveChangesAsync();
        }

        using var response = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-rollback");
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        responseBody.Should().NotContain("SIMULATION:");
        await using var verify = fixture.CreateContext();
        (await verify.Alerts.SingleAsync(alert => alert.Id == new AlertId(prepared.AlertId))).State
            .Should().Be(AlertState.PendingConfirmation);
        (await verify.AuditEvents.CountAsync(entry => entry.ResourceId == prepared.AlertId && entry.Action == "alert.confirmed"))
            .Should().Be(0);
        (await verify.IdempotencyRecords.CountAsync(record => record.IdempotencyKey == "phase6-confirm-rollback"))
            .Should().Be(0);
        (await verify.OutboxMessages.CountAsync(message => message.IdempotencyKey == stableOutboxKey)).Should().Be(1);
    }

    [Fact]
    public async Task ConfirmationDoesNotCopyClinicalOrDirectoryContentIntoDurabilityRecords()
    {
        const string patientSentinel = "SIM-PAT-CONFIRM-NONDISCLOSURE";
        const string sourceSentinel = "SIMULATION: CONFIRM-SOURCE-SENTINEL";
        const string approvedSentinel = "SIMULATION: CONFIRM-APPROVED-SENTINEL";
        const string endpointSentinel = "sim-secure://maya.chen";
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client, patientSentinel, sourceSentinel, approvedSentinel);
        fixture.ClearLogs();

        using var response = await ConfirmAsync(client, prepared.AlertId, prepared.Version, "phase6-confirm-nondisclosure");
        var body = await response.Content.ReadAsStringAsync();
        await using var db = fixture.CreateContext();
        var outbox = await db.OutboxMessages.SingleAsync(message => message.AggregateId == prepared.AlertId);
        var audit = await db.AuditEvents.SingleAsync(entry => entry.ResourceId == prepared.AlertId && entry.Action == "alert.confirmed");
        var idempotency = await db.IdempotencyRecords.SingleAsync(record => record.IdempotencyKey == "phase6-confirm-nondisclosure");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var value in new[] { patientSentinel, sourceSentinel, approvedSentinel, endpointSentinel })
        {
            body.Should().NotContain(value);
            outbox.PayloadJson.Should().NotContain(value);
            audit.SanitizedMetadata.Should().NotContain(value);
            idempotency.ResultReference.Should().NotContain(value);
            fixture.LogEntries.Should().NotContain(entry => entry.Contains(value, StringComparison.Ordinal));
        }
    }

    private readonly Dictionary<Guid, string> reviewedRevisions = [];

    private async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        Guid alertId,
        int expectedVersion,
        string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(expectedVersion, reviewedRevisions[alertId])),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    private async Task<PreparedAlert> CreateConfirmableAlertAsync(
        HttpClient client,
        string patientReference = "SIM-PAT-CONFIRM-0001",
        string sourceText = "SIMULATION: confirmation source",
        string approvedMessage = "SIMULATION: confirmation approved message")
    {
        using var create = await client.PostAsJsonAsync(
            "/api/v1/alerts/drafts",
            new CreateAlertDraftRequest(
                DemoDataSeeder.NorthSiteId.Value,
                DemoDataSeeder.EmergencyDepartmentId.Value,
                patientReference,
                "North Wing / Simulation Room 204",
                "Urgent",
                sourceText,
                new AlertSbarDraft(
                    "SIMULATION: confirmation situation",
                    "SIMULATION: confirmation background",
                    "SIMULATION: confirmation assessment",
                    "SIMULATION: confirmation recommendation"),
                [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var approved = await client.PutAsJsonAsync(
            $"/api/v1/alerts/{draft!.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, approvedMessage));
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();

        var maya = (await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/v1/directory/practitioners?q=Maya&includeInactive=false"))!.Single();
        using var recipients = await client.PutAsJsonAsync(
            $"/api/v1/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(
                approvedDraft!.DraftVersion,
                [new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "SecureMessage", maya.SelectionRevision)]));
        recipients.StatusCode.Should().Be(HttpStatusCode.OK, await recipients.Content.ReadAsStringAsync());
        var recipientDraft = await recipients.Content.ReadFromJsonAsync<AlertDraftView>();

        using var fieldConfirmation = await client.PostAsJsonAsync(
            $"/api/v1/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(recipientDraft!.DraftVersion, "heartRate", "118", "118", "beats/min"));
        fieldConfirmation.StatusCode.Should().Be(HttpStatusCode.OK, await fieldConfirmation.Content.ReadAsStringAsync());
        var confirmedDraft = await fieldConfirmation.Content.ReadFromJsonAsync<AlertDraftView>();

        using var submit = await client.PostAsJsonAsync(
            $"/api/v1/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmedDraft!.DraftVersion));
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await submit.Content.ReadFromJsonAsync<AlertDraftView>())!.State.Should().Be("PendingConfirmation");
        var review = await client.GetFromJsonAsync<AlertReviewView>($"/api/v1/alerts/{draft.AlertId:D}/review");
        reviewedRevisions[draft.AlertId] = review!.EscalationPlan.Revision;
        return new PreparedAlert(draft.AlertId, confirmedDraft.DraftVersion);
    }

    private sealed record PreparedAlert(Guid AlertId, int Version);
}
