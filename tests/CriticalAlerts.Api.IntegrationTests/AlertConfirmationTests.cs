using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertConfirmationTests(SeededPostgresApiFixture fixture)
{
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

    private static async Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        Guid alertId,
        int expectedVersion,
        string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/alerts/{alertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(expectedVersion)),
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
            "/api/alerts/drafts",
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
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        using var approved = await client.PutAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, approvedMessage));
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();
        approved.StatusCode.Should().Be(HttpStatusCode.OK);

        var maya = (await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/directory/practitioners?q=Maya&includeInactive=false"))!.Single();
        using var recipients = await client.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(
                approvedDraft!.DraftVersion,
                [new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "SecureMessage", maya.SelectionRevision)]));
        var recipientDraft = await recipients.Content.ReadFromJsonAsync<AlertDraftView>();
        recipients.StatusCode.Should().Be(HttpStatusCode.OK);

        using var fieldConfirmation = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(recipientDraft!.DraftVersion, "heartRate", "118", "118", "beats/min"));
        var confirmedDraft = await fieldConfirmation.Content.ReadFromJsonAsync<AlertDraftView>();
        fieldConfirmation.StatusCode.Should().Be(HttpStatusCode.OK);

        using var submit = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmedDraft!.DraftVersion));
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await submit.Content.ReadFromJsonAsync<AlertDraftView>())!.State.Should().Be("PendingConfirmation");
        return new PreparedAlert(draft.AlertId, confirmedDraft.DraftVersion);
    }

    private sealed record PreparedAlert(Guid AlertId, int Version);
}
