using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertLifecycleAuthorizationTests(SeededPostgresApiFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");

    [Fact]
    public async Task ResolveRequiresAnActiveResponsibilityAssignment()
    {
        var alert = await CreateActiveAlertAsync();
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var response = await SendLifecycleAsync(operatorClient, alert, "resolve-without-assignment", "resolve");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Should().Contain("responsibility-required");
    }

    [Fact]
    public async Task AcceptanceEnablesOperatorResolutionAndResolutionIsIdempotent()
    {
        var alert = await CreateActiveAlertAsync();
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var acceptance = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/my-alerts/{alert.AlertId:D}/responses")
        {
            Content = JsonContent.Create(new
            {
                expectedVersion = alert.Version,
                responseType = "Accepted",
            }),
        };
        acceptance.Headers.TryAddWithoutValidation("Idempotency-Key", $"accept-{alert.AlertId:N}");
        using var accepted = await practitioner.SendAsync(acceptance);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());

        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var resolved = await SendLifecycleAsync(operatorClient, alert, "resolve-after-acceptance", "resolve");
        using var replay = await SendLifecycleAsync(operatorClient, alert, "resolve-after-acceptance", "resolve");

        resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await resolved.Content.ReadFromJsonAsync<AlertLifecycleResult>())!.State.Should().Be("Resolved");
        (await replay.Content.ReadFromJsonAsync<AlertLifecycleResult>())!.Replayed.Should().BeTrue();

        await using var db = fixture.CreateContext();
        (await db.Alerts.SingleAsync(item => item.Id == new AlertId(alert.AlertId))).State.Should().Be(AlertState.Resolved);
    }

    [Fact]
    public async Task CancellationIsOrganizationScopedAndIdempotent()
    {
        var alert = await CreateActiveAlertAsync();
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var cancelled = await SendLifecycleAsync(operatorClient, alert, "cancel-alert", "cancel");
        using var replay = await SendLifecycleAsync(operatorClient, alert, "cancel-alert", "cancel");

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        (await replay.Content.ReadFromJsonAsync<AlertLifecycleResult>())!.Replayed.Should().BeTrue();

        var foreign = await fixture.CreateForeignOperatorDraftAsync();
        using var foreignClient = await fixture.CreateSignedInClientAsync(foreign.SimulationHandle);
        using var crossOrganization = await SendLifecycleAsync(foreignClient, alert, "foreign-cancel", "cancel");
        crossOrganization.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<PreparedAlert> CreateActiveAlertAsync()
    {
        await using var db = fixture.CreateContext();
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-LIFECYCLE",
            protector.Protect(
                "SIM-PAT-LIFECYCLE",
                new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value)),
            "North Wing Simulation Lifecycle Room",
            "DEMO-URGENT",
            AlertSourceType.Typed,
            protector.Protect(
                "SIMULATION: fictional lifecycle source",
                new SensitiveDataContext(ProtectedValuePurposes.AlertTypedSource, DemoDataSeeder.OrganizationId.Value)),
            Now,
            protector.Protect(
                "{\"situation\":\"SIMULATION: fictional lifecycle situation\"}",
                new SensitiveDataContext(ProtectedValuePurposes.AlertSbar, DemoDataSeeder.OrganizationId.Value)));
        alert.SetApprovedMessage(
            protector.Protect(
                "SIMULATION: fictional lifecycle approved message",
                new SensitiveDataContext(ProtectedValuePurposes.AlertApprovedMessage, DemoDataSeeder.OrganizationId.Value)),
            alert.DraftVersion,
            Now.AddMinutes(1));
        alert.ReplaceRecipients(
            [new ValidatedRecipientSelection(
                DemoDataSeeder.RileySatoId,
                null,
                NotificationChannel.SecureMessage,
                "SIM-REV-LIFECYCLE",
                Now,
                "Primary")],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now.AddMinutes(2));
        var practitioner = await db.Practitioners.SingleAsync(item => item.Id == DemoDataSeeder.RileySatoId);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, Now.AddMinutes(3));
        alert.ConfirmForDispatch(
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            [practitioner],
            Now.AddMinutes(4),
            $"lifecycle-confirm-{alert.Id.Value:N}");
        alert.MarkActive(Now.AddMinutes(5), $"lifecycle-active-{alert.Id.Value:N}");
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return new PreparedAlert(alert.Id.Value, alert.DraftVersion.Value);
    }

    private static async Task<HttpResponseMessage> SendLifecycleAsync(
        HttpClient client,
        PreparedAlert alert,
        string key,
        string action)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/alerts/{alert.AlertId:D}/{action}")
        {
            Content = JsonContent.Create(new AlertLifecycleActionRequest(alert.Version)),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private sealed record PreparedAlert(Guid AlertId, int Version);
}
