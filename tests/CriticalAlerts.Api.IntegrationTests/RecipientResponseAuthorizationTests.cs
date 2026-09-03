using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class RecipientResponseAuthorizationTests(SeededPostgresApiFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T14:00:00Z");

    [Fact]
    public async Task PractitionerInboxRoutesRequireAuthenticationAndPractitionerRole()
    {
        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync("/api/my-alerts");
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var operatorResponse = await operatorClient.GetAsync("/api/my-alerts");
        using var administratorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var administratorResponse = await administratorClient.GetAsync("/api/my-alerts");

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        administratorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LinkedPractitionerSeesOnlyAddressedActiveAlertAndProtectedDetail()
    {
        var addressed = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: true);
        var other = await CreateActiveAlertAsync(DemoDataSeeder.MayaChenId, includeSms: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var inboxResponse = await client.GetAsync("/api/my-alerts");
        var inboxBody = await inboxResponse.Content.ReadAsStringAsync();
        inboxResponse.StatusCode.Should().Be(HttpStatusCode.OK, inboxBody);
        var inbox = System.Text.Json.JsonSerializer.Deserialize<InboxItemDto[]>(
            inboxBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var detailResponse = await client.GetAsync($"/api/my-alerts/{addressed.AlertId:D}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<InboxDetailDto>();
        using var unaddressedResponse = await client.GetAsync($"/api/my-alerts/{other.AlertId:D}");

        inbox.Should().ContainSingle(item => item.AlertId == addressed.AlertId)
            .Which.ConfirmedVersion.Should().Be(addressed.Version);
        inbox.Should().NotContain(item => item.AlertId == other.AlertId);
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        detail.Should().NotBeNull();
        detail!.SimulationPatientReference.Should().Be("SIM-PAT-PHASE8");
        detail.ApprovedMessage.Should().Be("SIMULATION: fictional Phase 8 approved message");
        detail.Channels.Should().BeEquivalentTo("SecureMessage", "Sms");
        (await detailResponse.Content.ReadAsStringAsync()).Should().NotContain("sim-secure://");
        (await detailResponse.Content.ReadAsStringAsync()).ToLowerInvariant().Should().NotContain("ciphertext");
        unaddressedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenMarksOnlySecureMessageAndReplaysWithoutTrustingCallerIdentity()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: true);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var first = await SendOpenedAsync(client, prepared, "phase8-open-replay");
        var firstResult = await first.Content.ReadFromJsonAsync<OpenedResultDto>();
        using var replay = await SendOpenedAsync(client, prepared, "phase8-open-replay");
        var replayResult = await replay.Content.ReadFromJsonAsync<OpenedResultDto>();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResult!.Replayed.Should().BeFalse();
        replayResult!.Replayed.Should().BeTrue();
        firstResult.SecureMessageOpenedAtUtc.Should().NotBeNull();

        await using var db = fixture.CreateContext();
        var attempts = await db.DeliveryAttempts.Where(item => item.AlertId == new AlertId(prepared.AlertId)).ToArrayAsync();
        attempts.Single(item => item.Channel == NotificationChannel.SecureMessage).OpenedState.Should().Be(ObservationState.Occurred);
        attempts.Single(item => item.Channel == NotificationChannel.Sms).OpenedState.Should().Be(ObservationState.NotApplicable);
        (await db.AuditEvents.CountAsync(item => item.ResourceId == prepared.AlertId && item.Action == "recipient.opened")).Should().Be(1);
    }

    [Fact]
    public async Task OpenWithoutSecureMessageIsRejected()
    {
        var prepared = await CreateActiveAlertAsync(
            DemoDataSeeder.RileySatoId,
            includeSms: true,
            includeSecureMessage: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var response = await SendOpenedAsync(client, prepared, "phase8-open-not-supported");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("opened-not-supported");
    }

    [Fact]
    public async Task ResponseTypeMustBeExplicitAndIdempotencyKeyIsTrimmedBeforeLookup()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var numeric = await SendResponseAsync(client, prepared, "1", "phase8-numeric-response");
        using var first = await SendResponseAsync(client, prepared, "Acknowledged", " phase8-trimmed-key ");
        using var replay = await SendResponseAsync(client, prepared, "Acknowledged", "phase8-trimmed-key");

        numeric.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await numeric.Content.ReadAsStringAsync()).Should().Contain("response-type-invalid");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<ResponseResultDto>())!.Replayed.Should().BeTrue();
    }

    [Fact]
    public async Task AcknowledgementReplaysAndNeverCreatesResponsibility()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: true);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var first = await SendResponseAsync(client, prepared, "Acknowledged", "phase8-ack-replay");
        var firstResult = await first.Content.ReadFromJsonAsync<ResponseResultDto>();
        using var replay = await SendResponseAsync(client, prepared, "Acknowledged", "phase8-ack-replay");
        var replayResult = await replay.Content.ReadFromJsonAsync<ResponseResultDto>();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        firstResult!.ResponseType.Should().Be("Acknowledged");
        firstResult.ResponsibilityAcceptedAtUtc.Should().BeNull();
        firstResult.Replayed.Should().BeFalse();
        replayResult!.Replayed.Should().BeTrue();

        await using var db = fixture.CreateContext();
        (await db.RecipientResponses.CountAsync(item => item.AlertId == new AlertId(prepared.AlertId))).Should().Be(1);
        (await db.ResponsibilityAssignments.CountAsync(item => item.AlertId == new AlertId(prepared.AlertId))).Should().Be(0);
        (await db.Alerts.SingleAsync(item => item.Id == new AlertId(prepared.AlertId))).State.Should().Be(AlertState.Active);
    }

    [Fact]
    public async Task AcceptanceCreatesOneAssignmentAndConflictingDispositionIsRejected()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var accepted = await SendResponseAsync(client, prepared, "Accepted", "phase8-accepted");
        var acceptedResult = await accepted.Content.ReadFromJsonAsync<ResponseResultDto>();
        using var conflicting = await SendResponseAsync(client, prepared, "Declined", "phase8-declined-after-accept");

        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        acceptedResult!.ResponsibilityAcceptedAtUtc.Should().NotBeNull();
        conflicting.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflicting.Content.ReadAsStringAsync()).Should().Contain("terminal-disposition-conflict");

        await using var db = fixture.CreateContext();
        var response = await db.RecipientResponses.SingleAsync(item => item.AlertId == new AlertId(prepared.AlertId));
        var assignment = await db.ResponsibilityAssignments.SingleAsync(item => item.AlertId == new AlertId(prepared.AlertId));
        response.ResponseType.Should().Be(RecipientResponseType.Accepted);
        assignment.SourceResponseId.Should().Be(response.Id);
        assignment.PractitionerId.Should().Be(DemoDataSeeder.RileySatoId);
        (await db.Alerts.SingleAsync(item => item.Id == new AlertId(prepared.AlertId))).State.Should().Be(AlertState.Active);
    }

    [Fact]
    public async Task StaleVersionAndIdempotencyReuseReturnSafeConflicts()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var stale = await SendResponseAsync(
            client,
            prepared with { Version = prepared.Version + 1 },
            "Unavailable",
            "phase8-stale");
        using var first = await SendResponseAsync(client, prepared, "Declined", "phase8-key-conflict");
        using var reused = await SendResponseAsync(client, prepared, "Unavailable", "phase8-key-conflict");

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("alert-version-stale");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reused.Content.ReadAsStringAsync()).Should().Contain("idempotency-conflict");
    }

    [Fact]
    public async Task ResponseResultIsScopedToAuthenticatedPractitionerOnMultiRecipientAlert()
    {
        var prepared = await CreateActiveAlertAsync(
            DemoDataSeeder.RileySatoId,
            includeSms: false,
            DemoDataSeeder.MayaChenId);
        await using (var db = fixture.CreateContext())
        {
            var mayaResponse = RecipientResponse.Record(
                RecipientResponseId.New(),
                DemoDataSeeder.OrganizationId,
                new AlertId(prepared.AlertId),
                new AlertDraftVersion(prepared.Version),
                DemoDataSeeder.MayaChenId,
                RecipientResponseType.Accepted,
                DemoDataSeeder.JordanUserId,
                Now.AddMinutes(6),
                "simulation-responsibility-accepted");
            db.RecipientResponses.Add(mayaResponse);
            db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(mayaResponse)!);
            await db.SaveChangesAsync();
        }

        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var response = await SendResponseAsync(client, prepared, "Acknowledged", "phase8-multi-recipient-scope");
        var result = await response.Content.ReadFromJsonAsync<ResponseResultDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Should().NotBeNull();
        result!.AcknowledgedAtUtc.Should().NotBeNull();
        result.TerminalDisposition.Should().BeNull();
        result.ResponsibilityAcceptedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentSameResponseIsIdempotentAndCreatesOneAssignment()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: false);
        using var firstClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var secondClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        var requests = await Task.WhenAll(
            SendResponseAsync(firstClient, prepared, "Accepted", "phase8-concurrent-replay"),
            SendResponseAsync(secondClient, prepared, "Accepted", "phase8-concurrent-replay"));
        using var first = requests[0];
        using var second = requests[1];
        var firstResult = await first.Content.ReadFromJsonAsync<ResponseResultDto>();
        var secondResult = await second.Content.ReadFromJsonAsync<ResponseResultDto>();

        requests.Select(item => item.StatusCode).Should().OnlyContain(item => item == HttpStatusCode.OK);
        new[] { firstResult!.Replayed, secondResult!.Replayed }.Should().BeEquivalentTo([false, true]);
        await using var db = fixture.CreateContext();
        (await db.RecipientResponses.CountAsync(item => item.AlertId == new AlertId(prepared.AlertId))).Should().Be(1);
        (await db.ResponsibilityAssignments.CountAsync(item => item.AlertId == new AlertId(prepared.AlertId))).Should().Be(1);
        (await db.AuditEvents.CountAsync(item => item.ResourceId == prepared.AlertId
            && item.Action == "recipient.response.accepted")).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentDifferentTerminalResponsesCommitExactlyOneDisposition()
    {
        var prepared = await CreateActiveAlertAsync(DemoDataSeeder.RileySatoId, includeSms: false);
        using var firstClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var secondClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        var requests = await Task.WhenAll(
            SendResponseAsync(firstClient, prepared, "Accepted", "phase8-concurrent-accepted"),
            SendResponseAsync(secondClient, prepared, "Declined", "phase8-concurrent-declined"));
        using var first = requests[0];
        using var second = requests[1];

        requests.Select(item => item.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.Conflict]);
        await using var db = fixture.CreateContext();
        var terminal = await db.RecipientResponses.SingleAsync(item => item.AlertId == new AlertId(prepared.AlertId));
        var assignments = await db.ResponsibilityAssignments
            .Where(item => item.AlertId == new AlertId(prepared.AlertId))
            .ToArrayAsync();
        assignments.Should().HaveCount(terminal.ResponseType == RecipientResponseType.Accepted ? 1 : 0);
    }

    [Fact]
    public async Task SimulationResponseEndpointsFailClosedOutsideDevelopmentAndTest()
    {
        using var disabledFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
            builder.UseSetting("SimulationResponses:Enabled", "false");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        });
        using var disabledClient = disabledFactory.CreateClient();
        using var disabled = await disabledClient.GetAsync("/api/my-alerts");

        var enabled = () =>
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
                builder.UseSetting("SimulationResponses:Enabled", "true");
                builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            });
            using var client = factory.CreateClient();
        };

        disabled.StatusCode.Should().Be(HttpStatusCode.NotFound);
        enabled.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
    }

    private async Task<PreparedAlert> CreateActiveAlertAsync(
        PractitionerId practitionerId,
        bool includeSms,
        PractitionerId? additionalPractitionerId = null,
        bool includeSecureMessage = true)
    {
        await using var db = fixture.CreateContext();
        var practitioner = await db.Practitioners.SingleAsync(item => item.Id == practitionerId);
        var practitioners = new List<Practitioner> { practitioner };
        if (additionalPractitionerId is not null)
        {
            practitioners.Add(await db.Practitioners.SingleAsync(item => item.Id == additionalPractitionerId.Value));
        }

        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-PHASE8",
            "North Wing Simulation Room 8",
            "DEMO-URGENT",
            AlertSourceType.Typed,
            protector.Protect(
                "SIMULATION: fictional Phase 8 typed source",
                new SensitiveDataContext("alert-typed-source", DemoDataSeeder.OrganizationId.Value)),
            Now,
            protector.Protect(
                "{\"situation\":\"SIMULATION: fictional Phase 8 situation\"}",
                new SensitiveDataContext("alert-sbar", DemoDataSeeder.OrganizationId.Value)));
        alert.SetApprovedMessage(
            protector.Protect(
                "SIMULATION: fictional Phase 8 approved message",
                new SensitiveDataContext("alert-approved-message", DemoDataSeeder.OrganizationId.Value)),
            alert.DraftVersion,
            Now.AddMinutes(1));
        var selections = new List<ValidatedRecipientSelection>();
        if (includeSecureMessage)
        {
            selections.Add(new ValidatedRecipientSelection(
                practitioner.Id,
                null,
                NotificationChannel.SecureMessage,
                "SIM-REV-PHASE8",
                Now,
                "Primary"));
        }
        if (includeSms)
        {
            selections.Add(new ValidatedRecipientSelection(
                practitioner.Id,
                null,
                NotificationChannel.Sms,
                "SIM-REV-PHASE8",
                Now,
                "Primary"));
        }

        if (additionalPractitionerId is not null)
        {
            selections.Add(new ValidatedRecipientSelection(
                additionalPractitionerId.Value,
                null,
                NotificationChannel.SecureMessage,
                "SIM-REV-PHASE8",
                Now,
                "Backup"));
        }

        alert.ReplaceRecipients(selections, DemoDataSeeder.JordanUserId, alert.DraftVersion, Now.AddMinutes(2));
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, Now.AddMinutes(3));
        alert.ConfirmForDispatch(
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            practitioners,
            Now.AddMinutes(4),
            $"phase8-seed-{alert.Id.Value:N}");
        alert.MarkActive(Now.AddMinutes(5), $"phase8-active-{alert.Id.Value:N}");
        db.Alerts.Add(alert);
        foreach (var selection in alert.CurrentRecipients)
        {
            var attempt = DeliveryAttempt.CreateRequested(
                DeliveryAttemptId.New(),
                alert.OrganizationId,
                alert.Id,
                selection.Id,
                selection.Channel,
                1,
                $"phase8-attempt-{selection.Id.Value:N}",
                "simulation-provider",
                Now.AddMinutes(4));
            attempt.MarkDelivered(Now.AddMinutes(5));
            db.DeliveryAttempts.Add(attempt);
        }

        await db.SaveChangesAsync();
        return new PreparedAlert(alert.Id.Value, alert.DraftVersion.Value);
    }

    private static async Task<HttpResponseMessage> SendOpenedAsync(HttpClient client, PreparedAlert prepared, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/my-alerts/{prepared.AlertId:D}/opened")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = prepared.Version,
                organizationId = Guid.NewGuid(),
                practitionerId = DemoDataSeeder.MayaChenId.Value,
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendResponseAsync(
        HttpClient client,
        PreparedAlert prepared,
        string responseType,
        string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/my-alerts/{prepared.AlertId:D}/responses")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = prepared.Version,
                responseType,
                organizationId = Guid.NewGuid(),
                userId = DemoDataSeeder.MorganUserId.Value,
                practitionerId = DemoDataSeeder.MayaChenId.Value,
                role = "Administrator",
            }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed record PreparedAlert(Guid AlertId, int Version);

    private sealed record InboxItemDto(Guid AlertId, int ConfirmedVersion, string State);

    private sealed record InboxDetailDto(
        Guid AlertId,
        int ConfirmedVersion,
        string SimulationPatientReference,
        string ApprovedMessage,
        string[] Channels);

    private sealed record OpenedResultDto(
        Guid AlertId,
        int ConfirmedVersion,
        DateTimeOffset? SecureMessageOpenedAtUtc,
        bool Replayed);

    private sealed record ResponseResultDto(
        Guid AlertId,
        int ConfirmedVersion,
        string ResponseType,
        DateTimeOffset? AcknowledgedAtUtc,
        string? TerminalDisposition,
        DateTimeOffset? ResponsibilityAcceptedAtUtc,
        bool Replayed);
}
