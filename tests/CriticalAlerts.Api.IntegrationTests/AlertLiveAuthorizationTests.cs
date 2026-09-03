using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertLiveAuthorizationTests(SeededPostgresApiFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T16:00:00Z");

    [Fact]
    public async Task LiveRouteAllowsOperatorsAndAdministratorsButDeniesPractitionersAndAnonymousUsers()
    {
        var prepared = await CreateLiveAlertAsync();
        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var practitionerResponse = await practitioner.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var operatorResponse = await operatorClient.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");
        using var administrator = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var administratorResponse = await administrator.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        practitionerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        administratorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LiveProjectionSeparatesDeliveryOpenAcknowledgementDispositionAndResponsibilityWithoutProtectedValues()
    {
        var prepared = await CreateLiveAlertAsync();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var response = await client.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");
        var body = await response.Content.ReadAsStringAsync();
        var live = System.Text.Json.JsonSerializer.Deserialize<LiveAlertDto>(
            body,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        live.Should().NotBeNull();
        live!.AlertId.Should().Be(prepared.AlertId);
        live.ConfirmedVersion.Should().Be(prepared.Version);
        live.AlertState.Should().Be("Active");
        live.OutboxState.Should().Be("Processed");
        live.RefreshedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        live.Recipients.Should().HaveCount(2);

        var riley = live.Recipients.Single(item => item.PractitionerId == DemoDataSeeder.RileySatoId.Value);
        riley.DisplayName.Should().Be("Riley Sato");
        riley.SimulationCode.Should().Be("SIM-PRAC-0108");
        riley.AcknowledgedAtUtc.Should().NotBeNull();
        riley.TerminalDisposition.Should().Be("Accepted");
        riley.ResponsibilityAcceptedAtUtc.Should().NotBeNull();
        riley.Attempts.Should().ContainEquivalentOf(new
        {
            Channel = "SecureMessage",
            Status = "Delivered",
            OpenedState = "Occurred",
        });
        riley.Attempts.Single(item => item.Channel == "SecureMessage").OpenedAtUtc.Should().NotBeNull();
        riley.Attempts.Should().ContainEquivalentOf(new
        {
            Channel = "Sms",
            Status = "Failed",
            OpenedState = "NotApplicable",
            FailureCategory = "simulation-provider-rejected",
        });

        var maya = live.Recipients.Single(item => item.PractitionerId == DemoDataSeeder.MayaChenId.Value);
        maya.AcknowledgedAtUtc.Should().BeNull();
        maya.TerminalDisposition.Should().Be("Unavailable");
        maya.ResponsibilityAcceptedAtUtc.Should().BeNull();
        maya.Attempts.Should().ContainSingle(item => item.Channel == "Voice" && item.Status == "Submitted");

        body.Should().NotContain("SIM-PAT-LIVE-PROTECTED");
        body.Should().NotContain("SIMULATION: fictional live approved message");
        body.Should().NotContain("sim-secure://");
        body.Should().NotContain("phase8-provider-reference");
        body.ToLowerInvariant().Should().NotContain("ciphertext");
    }

    [Fact]
    public async Task LiveRouteReturnsNotFoundAcrossOrganizationBoundary()
    {
        var prepared = await CreateLiveAlertAsync();
        var foreign = await fixture.CreateForeignOperatorDraftAsync();
        using var foreignClient = await fixture.CreateSignedInClientAsync(foreign.SimulationHandle);

        using var response = await foreignClient.GetAsync($"/api/alerts/{prepared.AlertId:D}/live");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<PreparedLiveAlert> CreateLiveAlertAsync()
    {
        await using var db = fixture.CreateContext();
        var practitioners = await db.Practitioners
            .Where(item => item.Id == DemoDataSeeder.RileySatoId || item.Id == DemoDataSeeder.MayaChenId)
            .ToArrayAsync();
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-LIVE-PROTECTED",
            "North Wing Simulation Live Room",
            "DEMO-URGENT",
            AlertSourceType.Typed,
            protector.Protect(
                "SIMULATION: fictional protected live source",
                new SensitiveDataContext("alert-typed-source", DemoDataSeeder.OrganizationId.Value)),
            Now,
            protector.Protect(
                "{\"situation\":\"SIMULATION: fictional protected live situation\"}",
                new SensitiveDataContext("alert-sbar", DemoDataSeeder.OrganizationId.Value)));
        alert.SetApprovedMessage(
            protector.Protect(
                "SIMULATION: fictional live approved message",
                new SensitiveDataContext("alert-approved-message", DemoDataSeeder.OrganizationId.Value)),
            alert.DraftVersion,
            Now.AddMinutes(1));
        alert.ReplaceRecipients(
            [
                new ValidatedRecipientSelection(
                    DemoDataSeeder.RileySatoId,
                    null,
                    NotificationChannel.SecureMessage,
                    "SIM-REV-LIVE",
                    Now,
                    "Primary"),
                new ValidatedRecipientSelection(
                    DemoDataSeeder.RileySatoId,
                    null,
                    NotificationChannel.Sms,
                    "SIM-REV-LIVE",
                    Now,
                    "Primary"),
                new ValidatedRecipientSelection(
                    DemoDataSeeder.MayaChenId,
                    null,
                    NotificationChannel.Voice,
                    "SIM-REV-LIVE",
                    Now,
                    "Backup"),
            ],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now.AddMinutes(2));
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, Now.AddMinutes(3));
        alert.ConfirmForDispatch(
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            practitioners,
            Now.AddMinutes(4),
            $"phase8-live-confirm-{alert.Id.Value:N}");
        alert.MarkActive(Now.AddMinutes(5), $"phase8-live-active-{alert.Id.Value:N}");
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
                $"phase8-live-attempt-{selection.Id.Value:N}",
                "simulation-provider",
                Now.AddMinutes(4));
            if (selection.Channel == NotificationChannel.SecureMessage)
            {
                attempt.MarkSubmitted("phase8-provider-reference", Now.AddMinutes(5));
                attempt.MarkDelivered(Now.AddMinutes(6));
                attempt.MarkOpened(Now.AddMinutes(7));
            }
            else if (selection.Channel == NotificationChannel.Sms)
            {
                attempt.MarkFailed("simulation-provider-rejected", Now.AddMinutes(6));
            }
            else
            {
                attempt.MarkSubmitted("phase8-provider-reference", Now.AddMinutes(6));
            }

            db.DeliveryAttempts.Add(attempt);
        }

        var acknowledgement = RecipientResponse.Record(
            RecipientResponseId.New(),
            DemoDataSeeder.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            DemoDataSeeder.RileySatoId,
            RecipientResponseType.Acknowledged,
            DemoDataSeeder.RileyUserId,
            Now.AddMinutes(8),
            "simulation-acknowledged");
        var accepted = RecipientResponse.Record(
            RecipientResponseId.New(),
            DemoDataSeeder.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            DemoDataSeeder.RileySatoId,
            RecipientResponseType.Accepted,
            DemoDataSeeder.RileyUserId,
            Now.AddMinutes(9),
            "simulation-responsibility-accepted");
        var unavailable = RecipientResponse.Record(
            RecipientResponseId.New(),
            DemoDataSeeder.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            DemoDataSeeder.MayaChenId,
            RecipientResponseType.Unavailable,
            DemoDataSeeder.JordanUserId,
            Now.AddMinutes(8),
            "simulation-unavailable");
        db.RecipientResponses.AddRange(acknowledgement, accepted, unavailable);
        db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(accepted)!);

        var outbox = OutboxMessage.Create(
            OutboxMessageId.New(),
            DemoDataSeeder.OrganizationId,
            "AlertDispatchRequested",
            alert.Id.Value,
            $"{{\"alertId\":\"{alert.Id.Value:D}\",\"alertVersion\":{alert.DraftVersion.Value}}}",
            $"phase8-live-outbox-{alert.Id.Value:N}",
            Now.AddMinutes(4));
        outbox.TryAcquireLease("phase8-live-test", Now.AddMinutes(5), Now.AddMinutes(6)).Should().BeTrue();
        outbox.MarkProcessed("phase8-live-test", Now.AddMinutes(7));
        db.OutboxMessages.Add(outbox);

        await db.SaveChangesAsync();
        return new PreparedLiveAlert(alert.Id.Value, alert.DraftVersion.Value);
    }

    private sealed record PreparedLiveAlert(Guid AlertId, int Version);

    private sealed record LiveAlertDto(
        Guid AlertId,
        int ConfirmedVersion,
        string AlertState,
        string OutboxState,
        DateTimeOffset RefreshedAtUtc,
        LiveRecipientDto[] Recipients);

    private sealed record LiveRecipientDto(
        Guid PractitionerId,
        string SimulationCode,
        string DisplayName,
        string Specialty,
        string? OnCallSnapshot,
        DateTimeOffset? AcknowledgedAtUtc,
        string? TerminalDisposition,
        DateTimeOffset? ResponsibilityAcceptedAtUtc,
        LiveAttemptDto[] Attempts);

    private sealed record LiveAttemptDto(
        string Channel,
        int AttemptNumber,
        string Status,
        string OpenedState,
        DateTimeOffset? OpenedAtUtc,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? SubmittedAtUtc,
        DateTimeOffset? DeliveredAtUtc,
        DateTimeOffset? FailedAtUtc,
        string? FailureCategory);
}
