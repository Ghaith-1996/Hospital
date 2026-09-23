using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertConfirmationTests(SeededPostgresApiFixture fixture)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, EscalationPlanView> ReviewedPlans = new();

    [Fact]
    public async Task ConfirmedPlanCannotBeChangedOrDeletedThroughSql()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        using var confirmed = await ConfirmAsync(client, prepared.AlertId, prepared.Version, Guid.NewGuid().ToString("D"));
        confirmed.EnsureSuccessStatusCode();
        await using var db = fixture.CreateContext();
        var update = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE confirmed_escalation_plans SET revision = 'changed' WHERE alert_id = {prepared.AlertId}");
        var delete = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM confirmed_escalation_plans WHERE alert_id = {prepared.AlertId}");
        await update.Should().ThrowAsync<Npgsql.PostgresException>();
        await delete.Should().ThrowAsync<Npgsql.PostgresException>();
    }

    [Fact]
    public async Task DirectoryEvidenceChangedAfterReviewRequiresNewApproval()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        await using var db = fixture.CreateContext();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE practitioners SET specialty = 'SIMULATION changed evidence' WHERE id = {DemoDataSeeder.MayaChenId.Value}");
            using var response = await ConfirmAsync(client, prepared.AlertId, prepared.Version, Guid.NewGuid().ToString("D"));
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await db.ConfirmedEscalationPlans.AnyAsync(row => row.AlertId == new AlertId(prepared.AlertId))).Should().BeFalse();
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE practitioners SET specialty = 'Emergency' WHERE id = {DemoDataSeeder.MayaChenId.Value}");
        }
    }

    [Fact]
    public async Task ChangedPlanRevisionIsRejectedWithoutSavingApproval()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        var plan = ReviewedPlans[prepared.AlertId];
        using var command = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{prepared.AlertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(prepared.Version,
                plan.PolicyId, plan.PolicyVersion, new string('0', 64))),
        };
        command.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(command);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        (await db.ConfirmedEscalationPlans.AnyAsync(row => row.AlertId == new AlertId(prepared.AlertId))).Should().BeFalse();
    }

    [Fact]
    public async Task ExactApprovalPersistsOnceAndReplayBindsTheSamePlan()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        var plan = ReviewedPlans[prepared.AlertId];
        var key = Guid.NewGuid().ToString("D");
        using var first = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key);
        using var replay = await ConfirmAsync(client, prepared.AlertId, prepared.Version, key);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ConfirmAlertReviewResult>())!.Replayed.Should().BeTrue();
        await using var db = fixture.CreateContext();
        var snapshot = await db.ConfirmedEscalationPlans.SingleAsync(row => row.AlertId == new AlertId(prepared.AlertId));
        snapshot.AlertVersion.Value.Should().Be(prepared.Version);
        snapshot.PolicyId.Value.Should().Be(plan.PolicyId);
        snapshot.PolicyVersion.Should().Be(plan.PolicyVersion);
        snapshot.Revision.Should().Be(plan.Revision);
        snapshot.SnapshotJson.Should().NotContain("SIM-PAT").And.NotContain("ciphertext").And.NotContain("SIMULATION:");
        using var changed = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{prepared.AlertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(prepared.Version,
                plan.PolicyId, plan.PolicyVersion, new string('0', 64))),
        };
        changed.Headers.Add("Idempotency-Key", key);
        using var conflict = await client.SendAsync(changed);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task ConfirmationWithoutExactEscalationApprovalFailsClosed()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        using var command = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{prepared.AlertId:D}/confirm")
        {
            Content = JsonContent.Create(new { expectedVersion = prepared.Version }),
        };
        command.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(command);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        (await db.OutboxMessages.AnyAsync(row => row.AggregateId == prepared.AlertId)).Should().BeFalse();
        (await db.Alerts.SingleAsync(row => row.Id == new AlertId(prepared.AlertId))).State
            .Should().Be(AlertState.PendingConfirmation);
    }

    [Fact]
    public async Task BackupReviewAndApprovalUseTheMatchingAssignmentAndDepartmentRole()
    {
        await using var context = fixture.CreateContext();
        var connectionString = context.Database.GetConnectionString()!;
        await DatabaseOperations.ResetDemoAsync(connectionString, "Test", fixture.DataProtectionKey, confirmReset: true);
        try
        {
            var now = DateTimeOffset.UtcNow;
            now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
            var matchingStart = now.AddHours(-2);
            var matchingEnd = now.AddHours(2);
            var matchingSync = now.AddMinutes(-10);
            var emergencyRole = PractitionerRoleAssignment.Create(PractitionerRoleId.New(),
                DemoDataSeeder.OrganizationId, DemoDataSeeder.RileySatoId,
                DemoDataSeeder.EmergencyDepartmentId, "Fictional emergency cover", false,
                "SIM-DIRECTORY", "SIM-ROLE-RILEY-EMERGENCY");
            await using (var seed = fixture.CreateContext())
            {
                var medicine = await seed.Departments.SingleAsync(row => row.SimulationCode == "SIM-DEPT-MEDICINE");
                seed.PractitionerRoles.Add(emergencyRole);
                seed.OnCallAssignments.AddRange(
                    OnCallAssignment.Create(OnCallAssignmentId.New(), DemoDataSeeder.OrganizationId,
                        DemoDataSeeder.RileySatoId, DemoDataSeeder.NorthSiteId,
                        DemoDataSeeder.EmergencyDepartmentId, OnCallTier.Backup,
                        matchingStart, matchingEnd, "SIM-ROSTER", "SIM-MATCHED-BACKUP", matchingSync),
                    OnCallAssignment.Create(OnCallAssignmentId.New(), DemoDataSeeder.OrganizationId,
                        DemoDataSeeder.RileySatoId, DemoDataSeeder.NorthSiteId, medicine.Id,
                        OnCallTier.Backup, now.AddHours(-1), now.AddHours(3),
                        "SIM-ROSTER", "SIM-OTHER-DEPARTMENT", now.AddMinutes(-1)));
                await seed.SaveChangesAsync();
            }

            using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
            var prepared = await CreateConfirmableAlertAsync(client);
            var recipient = ReviewedPlans[prepared.AlertId].Steps.SelectMany(step => step.Recipients)
                .Single(row => row.PractitionerId == DemoDataSeeder.RileySatoId.Value);
            recipient.PractitionerRoleId.Should().Be(emergencyRole.Id.Value);
            recipient.RoleTitle.Should().Be("Fictional emergency cover");
            recipient.Department.Should().Be("Fictional Emergency Care");
            recipient.Site.Should().Be("North Wing Simulation Site");
            recipient.DirectorySourceUpdatedAtUtc.Should().Be(matchingSync);
            recipient.OnCallSnapshot.Should().Contain(matchingStart.ToString("yyyy-MM-ddTHH:mm"))
                .And.Contain(matchingEnd.ToString("yyyy-MM-ddTHH:mm"));
            recipient.OnCallEvidence.Should().NotBeNull();
            recipient.OnCallEvidence!.SourceSystem.Should().Be("SIM-ROSTER");
            recipient.OnCallEvidence.SourceRecordId.Should().Be("SIM-MATCHED-BACKUP");
            recipient.OnCallEvidence.StartsAtUtc.Should().Be(matchingStart);
            recipient.OnCallEvidence.EndsAtUtc.Should().Be(matchingEnd);
            recipient.OnCallEvidence.LastSynchronizedAtUtc.Should().Be(matchingSync);

            using var confirmed = await ConfirmAsync(client, prepared.AlertId, prepared.Version, Guid.NewGuid().ToString("D"));
            confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
            await using var verify = fixture.CreateContext();
            var approval = await verify.ConfirmedEscalationPlans.SingleAsync(row => row.AlertId == new AlertId(prepared.AlertId));
            var saved = JsonSerializer.Deserialize<EscalationPlanView>(approval.SnapshotJson)!;
            saved.Steps.SelectMany(step => step.Recipients).Single(row => row.PractitionerId == DemoDataSeeder.RileySatoId.Value)
                .Should().Be(recipient);
        }
        finally
        {
            await DatabaseOperations.ResetDemoAsync(connectionString, "Test", fixture.DataProtectionKey, confirmReset: true);
        }
    }

    [Fact]
    public async Task ReviewExposesExactDemoPolicyAndFutureBackupPlan()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var prepared = await CreateConfirmableAlertAsync(client);
        using var response = await client.GetAsync($"/api/v1/alerts/{prepared.AlertId:D}/review");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.TryGetProperty("escalationPlan", out var plan).Should().BeTrue();
        plan.GetProperty("policyId").GetGuid().Should().NotBeEmpty();
        plan.GetProperty("policyVersion").GetString().Should().StartWith("DEMO");
        plan.GetProperty("revision").GetString().Should().NotBeNullOrWhiteSpace();
        plan.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0);
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

    internal async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        Guid alertId,
        int expectedVersion,
        string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(expectedVersion,
                ReviewedPlans[alertId].PolicyId, ReviewedPlans[alertId].PolicyVersion, ReviewedPlans[alertId].Revision)),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request);
    }

    internal async Task<PreparedAlert> CreateConfirmableAlertAsync(
        HttpClient client,
        string patientReference = "SIM-PAT-CONFIRM-0001",
        string sourceText = "SIMULATION: confirmation source",
        string approvedMessage = "SIMULATION: confirmation approved message",
        string directoryName = "Maya")
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
            $"/api/v1/directory/practitioners?q={Uri.EscapeDataString(directoryName)}&includeInactive=false"))!.Single();
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
        if (review?.EscalationPlan is not null)
        {
            ReviewedPlans[draft.AlertId] = review.EscalationPlan;
        }
        return new PreparedAlert(draft.AlertId, confirmedDraft.DraftVersion);
    }

    internal sealed record PreparedAlert(Guid AlertId, int Version);
}
