using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class EscalationOverrideTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task AuthorizationScopeAndActorIdentityAreEnforced()
    {
        var alert = await CreateAlert();
        using var anonymous = fixture.CreateClient();
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        var foreign = await fixture.CreateForeignOperatorDraftAsync();
        using var foreignClient = await fixture.CreateSignedInClientAsync(foreign.SimulationHandle);
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var administrator = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        foreach (var action in new[] { "pause", "resume" })
        {
            using var unauthenticated = await Send(anonymous, alert, action, Guid.NewGuid().ToString());
            using var forbidden = await Send(practitioner, alert, action, Guid.NewGuid().ToString());
            using var outside = await Send(foreignClient, alert, action, Guid.NewGuid().ToString());
            unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            outside.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        var key = Guid.NewGuid().ToString();
        using var paused = await Send(operatorClient, alert, "pause", key);
        paused.StatusCode.Should().Be(HttpStatusCode.OK, await paused.Content.ReadAsStringAsync());
        using var anotherActor = await Send(administrator, alert, "pause", key);
        anotherActor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var resumed = await Send(administrator, alert, "resume", Guid.NewGuid().ToString());
        resumed.StatusCode.Should().Be(HttpStatusCode.OK, await resumed.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("no-run")]
    [InlineData("stale")]
    [InlineData("accepted")]
    [InlineData("future-accepted")]
    [InlineData("cancelled")]
    [InlineData("completed")]
    [InlineData("future-clock")]
    [InlineData("future-selection")]
    public async Task FreshControlsRejectIneligibleOrTerminalFactsEvenBeforeWorkerStops(string condition)
    {
        var alert = await CreateAlert(condition != "legacy", condition != "no-run");
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        if (condition is "accepted" or "future-accepted" or "cancelled" or "completed" or "future-clock" or "future-selection")
        {
            using var paused = await Send(client, alert, "pause", Guid.NewGuid().ToString());
            paused.StatusCode.Should().Be(HttpStatusCode.OK, await paused.Content.ReadAsStringAsync());
            await using var setup = fixture.CreateContext();
            if (condition is "accepted" or "future-accepted")
            {
                var now = await new DatabaseClock(setup).GetUtcNowAsync();
                var response = RecipientResponse.Record(RecipientResponseId.New(), alert.OrganizationId, alert.Id, alert.DraftVersion,
                    DemoDataSeeder.MayaChenId, RecipientResponseType.Accepted, DemoDataSeeder.JordanUserId,
                    condition == "future-accepted" ? now.AddHours(1) : now, "simulation-responsibility-accepted");
                setup.RecipientResponses.Add(response);
                setup.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
                await setup.SaveChangesAsync();
            }
            if (condition == "cancelled")
            {
                var saved = await setup.Alerts.Include(a => a.StateTransitions).SingleAsync(a => a.Id == alert.Id);
                saved.Cancel(DemoDataSeeder.JordanUserId, await new DatabaseClock(setup).GetUtcNowAsync(), "SIM-cancel");
                await setup.SaveChangesAsync();
            }
            if (condition == "completed") await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET state = 'Completed', outcome = 'Exhausted', completed_at_utc = clock_timestamp(), paused_at_utc = NULL, remaining_delay = NULL WHERE alert_id = {alert.Id.Value}");
            if (condition == "future-clock") await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET updated_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {alert.Id.Value}");
            if (condition == "future-selection") await setup.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_recipient_selections SET selected_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {alert.Id.Value}");
        }
        foreach (var action in new[] { "pause", "resume" })
        {
            using var result = await Send(client, alert, action, Guid.NewGuid().ToString(), version: condition == "stale" ? alert.DraftVersion.Value + 1 : null);
            result.StatusCode.Should().Be(HttpStatusCode.Conflict, await result.Content.ReadAsStringAsync());
        }
        if (condition is "legacy" or "no-run")
        {
            await using var verify = fixture.CreateContext();
            (await verify.EscalationRuns.CountAsync(r => r.AlertId == alert.Id)).Should().Be(0);
        }
    }

    [Fact]
    public async Task ReplaySurvivesLaterLifecycleStopAndFutureEvidenceWhileChangedPayloadConflicts()
    {
        var alert = await CreateAlert();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var pauseKey = Guid.NewGuid().ToString();
        var resumeKey = Guid.NewGuid().ToString();
        using var pause = await Send(client, alert, "pause", pauseKey);
        using var resume = await Send(client, alert, "resume", resumeKey);
        pause.StatusCode.Should().Be(HttpStatusCode.OK, await pause.Content.ReadAsStringAsync());
        resume.StatusCode.Should().Be(HttpStatusCode.OK, await resume.Content.ReadAsStringAsync());
        await using (var db = fixture.CreateContext())
        {
            var saved = await db.Alerts.Include(a => a.StateTransitions).SingleAsync(a => a.Id == alert.Id);
            saved.Cancel(DemoDataSeeder.JordanUserId, await new DatabaseClock(db).GetUtcNowAsync(), "SIM-stop");
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_recipient_selections SET selected_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {alert.Id.Value}");
        }
        using var pauseReplay = await Send(client, alert, "pause", pauseKey);
        using var resumeReplay = await Send(client, alert, "resume", resumeKey);
        pauseReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        resumeReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await pauseReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString().Should().Be("Paused");
        (await resumeReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString().Should().Be("Scheduled");
        using var changed = await Send(client, alert, "pause", pauseKey, "ManualCoordination");
        using var stale = await Send(client, alert, "pause", pauseKey, version: alert.DraftVersion.Value + 1);
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PauseResumeAndHistoricalReplayPreserveOriginalResultsAndExactlyOneEventPerCommand()
    {
        var alert = await CreateAlert();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var pauseKey = new string('a', 68) + alert.Id.Value.ToString("N");
        using var pause = await Send(client, alert, "pause", pauseKey);
        pause.StatusCode.Should().Be(HttpStatusCode.OK, await pause.Content.ReadAsStringAsync());
        var original = await pause.Content.ReadFromJsonAsync<JsonElement>();
        original.GetProperty("state").GetString().Should().Be("Paused");
        using var repeatedFreshPause = await Send(client, alert, "pause", Guid.NewGuid().ToString());
        repeatedFreshPause.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var resume = await Send(client, alert, "resume", Guid.NewGuid().ToString());
        resume.StatusCode.Should().Be(HttpStatusCode.OK, await resume.Content.ReadAsStringAsync());
        using var repeatedFreshResume = await Send(client, alert, "resume", Guid.NewGuid().ToString());
        repeatedFreshResume.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var replay = await Send(client, alert, "pause", pauseKey);
        replay.StatusCode.Should().Be(HttpStatusCode.OK, await replay.Content.ReadAsStringAsync());
        var retained = await replay.Content.ReadFromJsonAsync<JsonElement>();
        retained.GetProperty("state").GetString().Should().Be("Paused");
        retained.GetProperty("occurredAtUtc").GetString().Should().Be(original.GetProperty("occurredAtUtc").GetString());
        retained.GetProperty("replayed").GetBoolean().Should().BeTrue();
        using var reuse = await Send(client, alert, "resume", pauseKey);
        reuse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var db = fixture.CreateContext();
        (await db.EscalationEvents.CountAsync(e => e.AlertId == alert.Id && (e.EventType == EscalationEventType.Paused || e.EventType == EscalationEventType.Resumed))).Should().Be(2);
        (await db.AuditEvents.CountAsync(e => e.ResourceId == alert.Id.Value && e.Action.StartsWith("escalation.") && e.ActorType == "user")).Should().Be(2);
        (await db.EscalationRuns.SingleAsync(e => e.AlertId == alert.Id)).State.Should().Be(EscalationRunState.Scheduled);
        var events = await db.EscalationEvents.Where(e => e.AlertId == alert.Id && e.ActorUserId != null).OrderBy(e => e.OccurredAtUtc).ToArrayAsync();
        events.Select(e => e.OverrideReason).Should().Equal(EscalationOverrideReason.OperatorReview, EscalationOverrideReason.ReadyToResume);
        var audits = await db.AuditEvents.Where(e => e.ResourceId == alert.Id.Value && e.ActorType == "user" && e.Action.StartsWith("escalation.")).ToArrayAsync();
        audits.Select(e => e.CorrelationId).Should().BeEquivalentTo(events.Select(e => e.CorrelationId.ToString("D")));
        var stored = await db.IdempotencyRecords.Where(e => e.OperationType == "escalation-override" && e.IdempotencyKey == pauseKey).SingleAsync();
        stored.ResultReference.Length.Should().BeLessThanOrEqualTo(100);
        var evidence = JsonSerializer.Serialize(new { events, audits, stored });
        foreach (var excluded in new[] { "SIM-PAT-OVERRIDE", "SIMULATION message", "Ciphertext", "Phone", "ProviderReference" }) evidence.Should().NotContain(excluded);
    }

    [Theory]
    [InlineData(null, "OperatorReview", 400)]
    [InlineData("oversized", "OperatorReview", 400)]
    [InlineData("valid", null, 400)]
    [InlineData("valid", "SIM secret arbitrary text", 400)]
    [InlineData("valid", "1", 400)]
    public async Task RejectsMissingOrUnboundedKeyAndNonAllowlistedReason(string? key, string? reason, int status)
    {
        var alert = await CreateAlert();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var result = await Send(client, alert, "pause", key == "oversized" ? new string('k', 101) : key, reason);
        ((int)result.StatusCode).Should().Be(status);
        (await result.Content.ReadAsStringAsync()).Should().NotContain("SIM secret arbitrary text");
        await using var db = fixture.CreateContext();
        (await db.EscalationEvents.CountAsync(e => e.AlertId == alert.Id && e.EventType == EscalationEventType.Paused)).Should().Be(0);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, Alert alert, string action, string? key, string? reason = "OperatorReview", int? version = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alert.Id.Value:D}/escalation/{action}")
        { Content = JsonContent.Create(new { expectedVersion = version ?? alert.DraftVersion.Value, reasonCode = action == "resume" && reason == "OperatorReview" ? "ReadyToResume" : reason }) };
        if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private async Task<Alert> CreateAlert(bool eligible = true, bool schedule = true)
    {
        await using var db = fixture.CreateContext();
        var now = (await new DatabaseClock(db).GetUtcNowAsync()).AddMinutes(-5);
        static ProtectedValue Protect(string value, string purpose = "override-test") => new(Encoding.UTF8.GetBytes(value), "test-v1", purpose);
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId, "SIM-PAT-OVERRIDE", Protect("SIM-PAT-OVERRIDE", ProtectedValuePurposes.AlertPatientReference),
            "SIMULATION room", "DEMO-URGENT", AlertSourceType.Typed, Protect("SIMULATION source"), now, Protect("{\"situation\":\"SIMULATION override\"}"));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var primary = await db.Practitioners.SingleAsync(x => x.Id == DemoDataSeeder.MayaChenId);
        alert.SetApprovedMessage(Protect("SIMULATION message"), alert.DraftVersion, now);
        alert.ReplaceRecipients([new(primary.Id, null, NotificationChannel.SecureMessage, "DEMO-revision", now, "No assignment")], DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion, [primary], now, "override-test");
        if (eligible)
        {
            var backup = await db.Practitioners.SingleAsync(x => x.Id == DemoDataSeeder.RileySatoId);
            var role = await db.PractitionerRoles.FirstAsync(x => x.PractitionerId == backup.Id);
            var policy = await db.EscalationPolicies.FirstAsync(x => x.OrganizationId == alert.OrganizationId);
            var definition = new EscalationPlanDefinition(alert.OrganizationId.Value, alert.Id.Value, alert.DraftVersion.Value, policy.Id.Value, policy.Version,
                DemoEscalationSemantics.TriggerCondition, DemoEscalationSemantics.StopCondition, "DEMO-primary",
                [new(1, 60, 1, "DEMO backup", [new(backup.Id.Value, role.Id.Value, "Fictional Backup", "DEMO backup", "SecureMessage", "DEMO-revision", now, "No assignment")])]);
            var plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), DemoDataSeeder.JordanUserId, now);
            alert.BindExactEscalationPlan(plan);
            db.AlertEscalationPlans.Add(plan);
            db.AlertEscalationRecipientSnapshots.AddRange(AlertEscalationRecipientSnapshot.FromPlan(plan));
        }
        alert.MarkActive(now, "override-test");
        await db.SaveChangesAsync();
        if (eligible && schedule) await new EscalationScheduler(db).ScheduleAsync(alert.OrganizationId, alert.Id);
        return alert;
    }
}
