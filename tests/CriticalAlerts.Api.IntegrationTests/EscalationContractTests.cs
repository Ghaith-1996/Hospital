using System.Net;
using System.Net.Http.Json;
using System.Text;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class EscalationContractTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task ScheduledLiveProjectionUsesExactEvidenceDatabaseTimeAndDoesNotActivateFutureSnapshots()
    {
        var alert = await CreateAlertAsync([Step(1, 600, "SecureMessage")]);
        DateTimeOffset before;
        await using (var db = fixture.CreateContext()) before = await new DatabaseClock(db).GetUtcNowAsync();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var first = await client.GetAsync($"/api/v1/alerts/{alert.AlertId:D}/live");
        var body = await first.Content.ReadAsStringAsync();
        var live = await first.Content.ReadFromJsonAsync<LiveDto>();
        DateTimeOffset after;
        await using (var db = fixture.CreateContext()) after = await new DatabaseClock(db).GetUtcNowAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK, body);
        live.Should().NotBeNull();
        live!.RefreshedAtUtc.Should().BeCloseTo(after, TimeSpan.FromSeconds(2));
        live.Escalation.SimulationOnly.Should().BeTrue();
        live.Escalation.TimingAuthority.Should().Be("PostgreSQLUtc");
        live.Escalation.AutomaticEscalationEligible.Should().BeTrue();
        live.Escalation.PolicyId.Should().Be(alert.PolicyId);
        live.Escalation.PolicyVersion.Should().Be(alert.PolicyVersion);
        live.Escalation.PlanRevision.Should().Be(alert.PlanRevision);
        live.Escalation.RunState.Should().Be("Scheduled");
        live.Escalation.CurrentStep.Should().Be(1);
        live.Escalation.NextStepSequence.Should().Be(1);
        live.Escalation.TotalSteps.Should().Be(1);
        live.Escalation.NextEvaluationAtUtc.Should().NotBeNull();
        live.Escalation.CanPause.Should().BeTrue();
        live.Escalation.CanResume.Should().BeFalse();
        live.Escalation.Timeline.Select(item => item.EventType).Should().Equal("Scheduled");
        live.Recipients.Should().NotContain(item => item.PractitionerId == DemoDataSeeder.RileySatoId.Value);
        using var futureBackup = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var futureDetail = await futureBackup.GetAsync($"/api/v1/my-alerts/{alert.AlertId:D}");
        futureDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var futureInbox = await futureBackup.GetFromJsonAsync<MyAlertSummaryDto[]>("/api/v1/my-alerts/");
        futureInbox.Should().NotContain(item => item.AlertId == alert.AlertId);

        await using var verify = fixture.CreateContext();
        var beforeRead = await DurableCounts(verify, alert.AlertId);
        using var second = await client.GetAsync($"/api/v1/alerts/{alert.AlertId:D}/live");
        second.EnsureSuccessStatusCode();
        verify.ChangeTracker.Clear();
        (await DurableCounts(verify, alert.AlertId)).Should().Be(beforeRead);
        body.Should().NotContain("SIM-PAT-LIVE-ESCALATION");
        body.Should().NotContain("SIMULATION protected escalation message");
        body.Should().NotContain("sim-secure://");
        body.ToLowerInvariant().Should().NotContain("provider-reference");
        body.ToLowerInvariant().Should().NotContain("ciphertext");
        body.ToLowerInvariant().Should().NotContain("phone");
    }

    [Fact]
    public async Task ActivatedSelectionsKeepPerChannelStepProvenanceAndTiedTimelineSemanticOrder()
    {
        var alert = await CreateAlertAsync([Step(1, 0, "SecureMessage"), Step(2, 0, "Sms")]);
        await EnsureRileySmsEndpointAsync();
        (await ActivateNextStepAsync(alert.AlertId)).Should().BeTrue();
        (await ActivateNextStepAsync(alert.AlertId)).Should().BeTrue();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        var live = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");

        live.Should().NotBeNull();
        live!.Escalation.RunState.Should().Be("Completed");
        live.Escalation.TerminalOutcome.Should().Be("Exhausted");
        live.Escalation.Exhausted.Should().BeTrue();
        live.Escalation.CurrentStep.Should().Be(2);
        live.Escalation.NextStepSequence.Should().BeNull();
        live.Escalation.TotalSteps.Should().Be(2);
        live.Escalation.ManualFallbackRequired.Should().BeTrue();
        var backup = live.Recipients.Single(item => item.PractitionerId == DemoDataSeeder.RileySatoId.Value);
        backup.Selections.Should().HaveCount(2);
        backup.Selections.Single(item => item.Channel == "SecureMessage").Should().Match<SelectionDto>(item =>
            item.SelectionSource == "EscalationPolicy" && item.EscalationRunId == live.Escalation.RunId
            && item.EscalationStepSequence == 1 && item.EscalationPolicyId == alert.PolicyId
            && item.EscalationPolicyVersion == alert.PolicyVersion && item.EscalationPlanRevision == alert.PlanRevision);
        backup.Selections.Single(item => item.Channel == "Sms").EscalationStepSequence.Should().Be(2);

        var finalInstant = live.Escalation.Timeline.Max(item => item.OccurredAtUtc);
        live.Escalation.Timeline.Where(item => item.OccurredAtUtc == finalInstant).Select(item => item.EventType)
            .Should().Equal("StepDue", "RecipientActivated", "DispatchQueued", "Exhausted");
        live.Escalation.Timeline.Should().OnlyContain(item => item.RunId == live.Escalation.RunId
            && item.AlertVersion == alert.Version && item.PolicyId == alert.PolicyId
            && item.PolicyVersion == alert.PolicyVersion && item.PlanRevision == alert.PlanRevision);
    }

    [Fact]
    public async Task PauseAndResumeCapabilitiesFollowDurableStateAndLifecycleOperatorAuthorization()
    {
        var alert = await CreateAlertAsync([Step(1, 600, "SecureMessage")]);
        var auditorHandle = await CreateAuditorAsync();
        using var auditor = await fixture.CreateSignedInClientAsync(auditorHandle);
        var auditorLive = await auditor.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");
        auditorLive!.Escalation.CanPause.Should().BeFalse();
        auditorLive.Escalation.CanResume.Should().BeFalse();
        using var denied = await SendOverride(auditor, alert, "pause", "OperatorReview");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var paused = await SendOverrideAfterClockRecovery(operatorClient, alert, "pause", "OperatorReview");
        paused.EnsureSuccessStatusCode();
        var pauseView = await operatorClient.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");
        pauseView!.Escalation.RunState.Should().Be("Paused");
        pauseView.Escalation.Paused.Should().BeTrue();
        pauseView.Escalation.RemainingPauseSeconds.Should().BeGreaterThan(0);
        pauseView.Escalation.NextEvaluationAtUtc.Should().BeNull();
        pauseView.Escalation.CanPause.Should().BeFalse();
        pauseView.Escalation.CanResume.Should().BeTrue();
        pauseView.Escalation.Timeline.Last().OverrideReason.Should().Be("OperatorReview");

        using var resumed = await SendOverrideAfterClockRecovery(operatorClient, alert, "resume", "ReadyToResume");
        resumed.EnsureSuccessStatusCode();
        var resumeView = await operatorClient.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");
        resumeView!.Escalation.RunState.Should().Be("Scheduled");
        resumeView.Escalation.RemainingPauseSeconds.Should().BeNull();
        resumeView.Escalation.NextEvaluationAtUtc.Should().NotBeNull();
        resumeView.Escalation.CanPause.Should().BeTrue();
        resumeView.Escalation.CanResume.Should().BeFalse();
        resumeView.Escalation.Timeline.TakeLast(2).Select(item => item.OverrideReason)
            .Should().Equal("OperatorReview", "ReadyToResume");

        await AddFutureResponsibilityAsync(alert);
        var futureAccepted = await operatorClient.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");
        futureAccepted!.Escalation.CanPause.Should().BeFalse();
        futureAccepted.Escalation.CanResume.Should().BeFalse();
    }

    [Fact]
    public async Task LegacyAlertIsIneligibleWithoutFabricatedPolicyRunOrControls()
    {
        var alert = await CreateAlertAsync([], exact: false);
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        var live = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");

        live!.Escalation.AutomaticEscalationEligible.Should().BeFalse();
        live.Escalation.PolicyId.Should().BeNull();
        live.Escalation.PolicyVersion.Should().BeNull();
        live.Escalation.PlanRevision.Should().BeNull();
        live.Escalation.RunId.Should().BeNull();
        live.Escalation.RunState.Should().BeNull();
        live.Escalation.CanPause.Should().BeFalse();
        live.Escalation.CanResume.Should().BeFalse();
        live.Escalation.Timeline.Should().BeEmpty();
    }

    [Theory]
    [InlineData("responsibility", "ResponsibilityAccepted", "responsibility-accepted", "StoppedByResponsibility")]
    [InlineData("resolved", "Resolved", "alert-resolved", "StoppedByResolution")]
    [InlineData("cancelled", "Cancelled", "alert-cancelled", "StoppedByCancellation")]
    public async Task StopConditionsProjectTerminalOutcomeSafeReasonAndTimeline(
        string fact,
        string outcome,
        string reason,
        string eventType)
    {
        var alert = await CreateAlertAsync([Step(1, 600, "SecureMessage")]);
        await AddStopFactAsync(alert, fact);
        (await ActivateNextStepAsync(alert.AlertId)).Should().BeTrue();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        var live = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");

        live!.Escalation.RunState.Should().Be("Stopped");
        live.Escalation.Stopped.Should().BeTrue();
        live.Escalation.TerminalOutcome.Should().Be(outcome);
        live.Escalation.ReasonCode.Should().Be(reason);
        live.Escalation.NextStepSequence.Should().BeNull();
        live.Escalation.NextEvaluationAtUtc.Should().BeNull();
        live.Escalation.ManualFallbackRequired.Should().BeFalse();
        live.Escalation.CanPause.Should().BeFalse();
        live.Escalation.CanResume.Should().BeFalse();
        live.Escalation.Timeline.Last().EventType.Should().Be(eventType);
    }

    [Fact]
    public async Task ProcessingFailureAndEscalationOutboxFailureRemainVisibleWithoutChangingOriginalOutboxState()
    {
        var processing = await CreateAlertAsync([Step(1, 0, "SecureMessage")]);
        await SetRileyActiveAsync(false);
        try
        {
            (await ActivateNextStepAsync(processing.AlertId)).Should().BeTrue();
        }
        finally
        {
            await SetRileyActiveAsync(true);
        }
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var failed = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{processing.AlertId:D}/live");
        failed!.AlertState.Should().Be("Active");
        failed.OutboxState.Should().Be("Processed");
        failed.Escalation.RunState.Should().Be("Stopped");
        failed.Escalation.TerminalOutcome.Should().Be("ProcessingFailed");
        failed.Escalation.FailureCategory.Should().Be("ConfirmedRecipientUnavailable");
        failed.Escalation.ManualFallbackRequired.Should().BeTrue();
        failed.Escalation.Timeline.Last().EventType.Should().Be("ProcessingFailed");
        failed.Recipients.Should().NotContain(item => item.PractitionerId == DemoDataSeeder.RileySatoId.Value);

        var dispatch = await CreateAlertAsync([Step(1, 0, "SecureMessage")]);
        (await ActivateNextStepAsync(dispatch.AlertId)).Should().BeTrue();
        await FailEscalationOutboxAsync(dispatch.AlertId);
        var deliveryFailed = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{dispatch.AlertId:D}/live");
        deliveryFailed!.AlertState.Should().Be("Active");
        deliveryFailed.OutboxState.Should().Be("Processed");
        deliveryFailed.Escalation.EscalationOutboxState.Should().Be("Failed");
        deliveryFailed.Escalation.EscalationOutboxFailureCategory.Should().Be("simulation-provider-outage");
        deliveryFailed.Escalation.ManualFallbackRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ExhaustedHistoryRemainsTerminalWhileLaterResponsibilityRemovesFallbackAction()
    {
        var alert = await CreateAlertAsync([Step(1, 0, "SecureMessage")]);
        (await ActivateNextStepAsync(alert.AlertId)).Should().BeTrue();
        await using (var db = fixture.CreateContext())
        {
            var now = await new DatabaseClock(db).GetUtcNowAsync();
            var response = RecipientResponse.Record(RecipientResponseId.New(), DemoDataSeeder.OrganizationId,
                new AlertId(alert.AlertId), new AlertDraftVersion(alert.Version), DemoDataSeeder.MayaChenId,
                RecipientResponseType.Accepted, DemoDataSeeder.JordanUserId, now.AddSeconds(-1), "simulation-responsibility-accepted");
            db.RecipientResponses.Add(response);
            db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
            await db.SaveChangesAsync();
        }
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        var live = await client.GetFromJsonAsync<LiveDto>($"/api/v1/alerts/{alert.AlertId:D}/live");

        live!.Escalation.RunState.Should().Be("Completed");
        live.Escalation.TerminalOutcome.Should().Be("Exhausted");
        live.Escalation.Exhausted.Should().BeTrue();
        live.Escalation.ManualFallbackRequired.Should().BeFalse();
        live.ManualFallbackRequired.Should().BeFalse();
        live.Escalation.CanPause.Should().BeFalse();
        live.Escalation.CanResume.Should().BeFalse();
    }

    private async Task<PreparedAlert> CreateAlertAsync(IReadOnlyList<EscalationStepSnapshot> steps, bool exact = true)
    {
        await using var db = fixture.CreateContext();
        var now = (await new DatabaseClock(db).GetUtcNowAsync()).AddMinutes(-5);
        static ProtectedValue Protect(string value, string purpose = "live-escalation-test") =>
            new(Encoding.UTF8.GetBytes(value), "test-v1", purpose);
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId, DemoDataSeeder.JordanUserId, "SIM-PAT-LIVE-ESCALATION",
            Protect("SIM-PAT-LIVE-ESCALATION", ProtectedValuePurposes.AlertPatientReference), "SIMULATION room", "DEMO-URGENT",
            AlertSourceType.Typed, Protect("SIMULATION protected source"), now,
            Protect("{\"situation\":\"SIMULATION protected escalation\"}"));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var primary = await db.Practitioners.SingleAsync(item => item.Id == DemoDataSeeder.MayaChenId);
        alert.SetApprovedMessage(Protect("SIMULATION protected escalation message"), alert.DraftVersion, now);
        alert.ReplaceRecipients([new(primary.Id, null, NotificationChannel.Sms, "DEMO-live-revision", now, "Primary")],
            DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, now);
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion, [primary], now, "live-escalation-test");
        AlertEscalationPlan? plan = null;
        if (exact)
        {
            var policy = await db.EscalationPolicies.FirstAsync(item => item.OrganizationId == alert.OrganizationId);
            var definition = new EscalationPlanDefinition(alert.OrganizationId.Value, alert.Id.Value, alert.DraftVersion.Value,
                policy.Id.Value, policy.Version, DemoEscalationSemantics.TriggerCondition, DemoEscalationSemantics.StopCondition,
                "DEMO-primary", steps);
            plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition),
                DemoDataSeeder.JordanUserId, now);
            alert.BindExactEscalationPlan(plan);
            db.AlertEscalationPlans.Add(plan);
            db.AlertEscalationRecipientSnapshots.AddRange(AlertEscalationRecipientSnapshot.FromPlan(plan));
        }
        alert.MarkActive(now, "live-escalation-test");
        var originalOutbox = OutboxMessage.Create(OutboxMessageId.New(), alert.OrganizationId, "AlertDispatchRequested",
            alert.Id.Value, $"{{\"alertId\":\"{alert.Id.Value:D}\",\"alertVersion\":{alert.DraftVersion.Value}}}",
            $"live-original-{alert.Id.Value:N}", now);
        originalOutbox.TryAcquireLease("live-test", now, now.AddMinutes(1)).Should().BeTrue();
        originalOutbox.MarkProcessed("live-test", now);
        db.OutboxMessages.Add(originalOutbox);
        await db.SaveChangesAsync();
        if (exact) await new EscalationScheduler(db).ScheduleAsync(alert.OrganizationId, alert.Id);
        return new(alert.Id.Value, alert.DraftVersion.Value, plan?.EscalationPolicyId.Value,
            plan?.EscalationPolicyVersion, plan?.Revision);
    }

    private static EscalationStepSnapshot Step(int sequence, long delaySeconds, string channel) =>
        new(sequence, delaySeconds, 1, "DEMO backup",
            [new(DemoDataSeeder.RileySatoId.Value, Guid.Parse("11111111-1111-4111-8111-111111110710"),
                "Fictional Riley Sato", "DEMO backup", channel, "DEMO-live-revision",
                DateTimeOffset.Parse("2026-08-01T12:00:00Z"), "No assignment")]);

    private async Task<bool> ActivateNextStepAsync(Guid alertId)
    {
        await using (var due = fixture.CreateContext())
            await due.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 minute', updated_at_utc = clock_timestamp() - interval '1 minute' WHERE alert_id = {alertId}");
        await using var db = fixture.CreateContext();
        var runId = await db.EscalationRuns.Where(item => item.AlertId == new AlertId(alertId)).Select(item => item.Id).SingleAsync();
        var claim = await new EscalationRunRepository(db).TryClaimAsync(runId, "live-contract", TimeSpan.FromMinutes(1));
        claim.Should().NotBeNull();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await new EscalationRunProcessor(db).ProcessClaimAsync(claim!, timeout.Token))
            await Task.Delay(20, timeout.Token);
        return true;
    }

    private async Task EnsureRileySmsEndpointAsync()
    {
        await using var db = fixture.CreateContext();
        if (await db.ContactEndpoints.AnyAsync(item => item.OrganizationId == DemoDataSeeder.OrganizationId
            && item.PractitionerId == DemoDataSeeder.RileySatoId && item.Kind == ContactEndpointKind.Sms)) return;
        db.ContactEndpoints.Add(ContactEndpoint.Create(ContactEndpointId.New(), DemoDataSeeder.OrganizationId,
            DemoDataSeeder.RileySatoId, ContactEndpointKind.Sms,
            new ProtectedValue(Encoding.UTF8.GetBytes("SIM-RILEY-SMS"), "test-v1", "live-escalation-endpoint"),
            "SIM-SMS-LIVE-RILEY", true, "SIM-LIVE", "SIM-RILEY-SMS"));
        await db.SaveChangesAsync();
    }

    private async Task SetRileyActiveAsync(bool active)
    {
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE practitioners SET is_active = {active} WHERE organization_id = {DemoDataSeeder.OrganizationId.Value} AND id = {DemoDataSeeder.RileySatoId.Value}");
    }

    private async Task AddStopFactAsync(PreparedAlert alert, string fact)
    {
        await using var db = fixture.CreateContext();
        var now = await new DatabaseClock(db).GetUtcNowAsync();
        if (fact is "responsibility" or "resolved")
        {
            var response = RecipientResponse.Record(RecipientResponseId.New(), DemoDataSeeder.OrganizationId,
                new AlertId(alert.AlertId), new AlertDraftVersion(alert.Version), DemoDataSeeder.MayaChenId,
                RecipientResponseType.Accepted, DemoDataSeeder.JordanUserId, now.AddSeconds(-1), "simulation-responsibility-accepted");
            db.RecipientResponses.Add(response);
            db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
        }
        if (fact is "resolved" or "cancelled")
        {
            var saved = await db.Alerts.Include(item => item.StateTransitions)
                .SingleAsync(item => item.Id == new AlertId(alert.AlertId));
            if (fact == "resolved") saved.Resolve(DemoDataSeeder.JordanUserId, now, "SIM-live-resolved");
            else saved.Cancel(DemoDataSeeder.JordanUserId, now, "SIM-live-cancelled");
        }
        await db.SaveChangesAsync();
    }

    private async Task AddFutureResponsibilityAsync(PreparedAlert alert)
    {
        await using var db = fixture.CreateContext();
        var now = await new DatabaseClock(db).GetUtcNowAsync();
        var response = RecipientResponse.Record(RecipientResponseId.New(), DemoDataSeeder.OrganizationId,
            new AlertId(alert.AlertId), new AlertDraftVersion(alert.Version), DemoDataSeeder.MayaChenId,
            RecipientResponseType.Accepted, DemoDataSeeder.JordanUserId, now.AddHours(1),
            "simulation-responsibility-accepted");
        db.RecipientResponses.Add(response);
        db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
        await db.SaveChangesAsync();
    }

    private async Task FailEscalationOutboxAsync(Guid alertId)
    {
        await using var db = fixture.CreateContext();
        var outbox = await db.OutboxMessages.SingleAsync(item => item.AggregateId == alertId && item.EventType == "EscalationDispatchRequested");
        var now = await new DatabaseClock(db).GetUtcNowAsync();
        outbox.TryAcquireLease("live-outbox-failure", now, now.AddMinutes(1)).Should().BeTrue();
        outbox.MarkFailed("live-outbox-failure", now, "simulation-provider-outage");
        await db.SaveChangesAsync();
    }

    private async Task<string> CreateAuditorAsync()
    {
        await using var db = fixture.CreateContext();
        var id = UserId.New();
        var handle = $"sim-auditor-{id.Value:N}";
        db.Users.Add(CriticalAlerts.Domain.Identity.UserAccount.CreateSimulation(id, DemoDataSeeder.OrganizationId,
            "Fictional Simulation Auditor", handle, DateTimeOffset.Parse("2026-08-01T12:00:00Z")));
        db.UserRoles.Add(CriticalAlerts.Domain.Identity.UserRole.Create(DemoDataSeeder.OrganizationId, id, DemoDataSeeder.AuditorRoleId));
        await db.SaveChangesAsync();
        return handle;
    }

    private static async Task<HttpResponseMessage> SendOverride(
        HttpClient client,
        PreparedAlert alert,
        string action,
        string reason,
        string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alert.AlertId:D}/escalation/{action}")
        { Content = JsonContent.Create(new { expectedVersion = alert.Version, reasonCode = reason }) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendOverrideAfterClockRecovery(
        HttpClient client,
        PreparedAlert alert,
        string action,
        string reason)
    {
        var key = Guid.NewGuid().ToString("N");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            var response = await SendOverride(client, alert, action, reason, key);
            if (response.StatusCode != HttpStatusCode.Conflict
                || !string.Equals(
                    (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("detail").GetString(),
                    "escalation-clock-conflict",
                    StringComparison.Ordinal)
                || DateTimeOffset.UtcNow >= deadline)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(25);
        }
    }

    private static async Task<(int Selections, int Events, int Runs)> DurableCounts(CriticalAlertsDbContext db, Guid alertId) =>
        (await db.AlertRecipientSelections.CountAsync(item => item.AlertId == new AlertId(alertId)),
         await db.EscalationEvents.CountAsync(item => item.AlertId == new AlertId(alertId)),
         await db.EscalationRuns.CountAsync(item => item.AlertId == new AlertId(alertId)));

    private sealed record PreparedAlert(Guid AlertId, int Version, Guid? PolicyId, string? PolicyVersion, string? PlanRevision);
    private sealed record LiveDto(Guid AlertId, int ConfirmedVersion, string AlertState, string OutboxState,
        DateTimeOffset RefreshedAtUtc, bool ManualFallbackRequired, RecipientDto[] Recipients, EscalationDto Escalation);
    private sealed record RecipientDto(Guid PractitionerId, SelectionDto[] Selections);
    private sealed record SelectionDto(Guid SelectionId, string Channel, string SelectionSource, Guid? EscalationRunId,
        int? EscalationStepSequence, Guid? EscalationPolicyId, string? EscalationPolicyVersion, string? EscalationPlanRevision);
    private sealed record EscalationDto(bool SimulationOnly, string TimingAuthority, bool AutomaticEscalationEligible,
        Guid? PolicyId, string? PolicyVersion, string? PlanRevision, Guid? RunId, string? RunState, int? CurrentStep,
        int? NextStepSequence, int? TotalSteps, DateTimeOffset? NextEvaluationAtUtc, long? RemainingPauseSeconds,
        bool Paused, bool Stopped, bool Exhausted, string? TerminalOutcome, string? ReasonCode, string? FailureCategory,
        string EscalationOutboxState, string? EscalationOutboxFailureCategory, bool ManualFallbackRequired,
        bool CanPause, bool CanResume, TimelineDto[] Timeline);
    private sealed record TimelineDto(Guid EventId, Guid RunId, int AlertVersion, Guid PolicyId, string PolicyVersion,
        string PlanRevision, int StepSequence, string EventType, DateTimeOffset OccurredAtUtc,
        Guid? RecipientSelectionId, string? FailureCategory, string? OverrideReason);
    private sealed record MyAlertSummaryDto(Guid AlertId);
}
