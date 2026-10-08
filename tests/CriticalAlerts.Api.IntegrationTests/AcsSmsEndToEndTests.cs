using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

/// <summary>
/// Phase 12 end-to-end flow (F1, F9, F14, F18-F27): real API host, real PostgreSQL, the real outbox processor,
/// the ACS SMS adapter against a signature-verifying fake ACS endpoint, and signed Entra webhook tokens.
/// Writes TestResults/phase12/acs-sms-e2e-evidence.json with safe identifiers and counts only.
/// </summary>
[Collection(AcsSmsEndToEndCollection.Name)]
public sealed class AcsSmsEndToEndTests(AcsSmsEndToEndFixture fixture)
{
    [Fact]
    public async Task DeliveredReportClosesTheDeliveryLoopWithoutImplyingAcknowledgementOrAcceptance()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);

        var first = await fixture.ProcessAsync();
        first.Outcome.Should().Be("rescheduled");
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        attempt.Provider.Should().Be(AzureCommunicationServicesSmsChannel.Provider);
        attempt.ProviderReference.Should().StartWith("acs-e2e-");
        fixture.Transport.Requests.Should().ContainSingle().Which.SignatureValid.Should().BeTrue();
        var tag = AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey);
        for (var poll = 0; poll < 3; poll++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(6));
            (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        }

        fixture.Transport.Requests.Should().ContainSingle("waiting for the delivery report never resends");
        await using (var waiting = fixture.CreateContext())
            (await waiting.AuditEvents.CountAsync(row => row.ResourceId == alertId && row.Action == "dispatch.retry-scheduled"))
                .Should().Be(1, "only the start of a delivery-report wait is audited");

        using (var mismatch = await fixture.PostReportsAsync(fixture.ValidToken(), Report("evt-tag-mismatch", attempt.ProviderReference, "Delivered", "ca-" + new string('0', 32))))
            mismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);

        using (var delivered = await fixture.PostReportsAsync(fixture.ValidToken(), Report("evt-delivered", attempt.ProviderReference, "Delivered", tag)))
            delivered.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var duplicate = await fixture.PostReportsAsync(fixture.ValidToken(), Report("evt-delivered", attempt.ProviderReference, "Delivered", tag)))
            duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var late = await fixture.PostReportsAsync(fixture.ValidToken(), Report("evt-late-failed", attempt.ProviderReference, "Failed", tag)))
            late.StatusCode.Should().Be(HttpStatusCode.OK);
        var unknownReport = Report("evt-unknown", "acs-not-ours-0001", "Delivered", tag);
        ((Dictionary<string, object>)unknownReport[0])["eventTime"] = DateTime.UtcNow.AddMinutes(-15).ToString("O");
        using (var unknown = await fixture.PostReportsAsync(fixture.ValidToken(), unknownReport))
            unknown.StatusCode.Should().Be(HttpStatusCode.OK);

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        (await fixture.ProcessAsync()).Outcome.Should().Be("processed");

        await using var db = fixture.CreateContext();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        final.FailureCategory.Should().BeEmpty();
        var events = await db.DeliveryEvents.Where(row => row.DeliveryAttemptId == final.Id).OrderBy(row => row.ReceivedAtUtc).ToArrayAsync();
        events.Select(row => row.ProviderEventId).Should().BeEquivalentTo(
            [$"acs-sms:{tag}:submitted", "acs-eg:evt-delivered", "acs-eg:evt-late-failed"]);
        var inbox = await db.InboxMessages.Where(row => row.Handler == ProviderDeliveryReportService.AcsSmsHandler).ToArrayAsync();
        inbox.Select(row => (row.ExternalMessageId, row.Result)).Should().BeEquivalentTo(
            [("evt-tag-mismatch", "rejected-tag-mismatch"), ("evt-delivered", "applied"), ("evt-late-failed", "no-state-change")]);
        (await db.OutboxMessages.SingleAsync(row => row.AggregateId == alertId)).ProcessingState.Should().Be(OutboxProcessingState.Processed);
        (await db.AuditEvents.CountAsync(row => row.ActorType == "provider-webhook")).Should().Be(3);

        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        var recipient = live.GetProperty("recipients").EnumerateArray().Single();
        recipient.GetProperty("attempts").EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Delivered");
        recipient.GetProperty("attempts").EnumerateArray().Single().GetProperty("openedState").GetString().Should().Be("NotApplicable");
        recipient.GetProperty("acknowledgedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        recipient.GetProperty("responsibilityAcceptedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        live.GetProperty("alertState").GetString().Should().Be("Active");

        await fixture.AssertNoSensitiveValuesPersistedOrLoggedAsync();
        fixture.Record("delivered-closed-loop", new
        {
            attemptStatus = final.Status.ToString(),
            deliveryEvents = events.Length,
            inboxResults = inbox.Select(row => row.Result).OrderBy(value => value).ToArray(),
            outbox = "Processed",
            liveAcknowledged = false,
            liveResponsibilityAccepted = false,
            providerRequests = fixture.Transport.Requests.Count,
            signaturesValid = fixture.Transport.Requests.All(row => row.SignatureValid),
        });
    }

    [Fact]
    public async Task ReportRacingTheWorkerCommitIsDeferredForRedeliveryInsteadOfAcknowledged()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await using (var peek = fixture.CreateContext())
        {
            // The fake issues acs-e2e-0001 for the first send; the tag is derived from the durable attempt key.
            (await peek.DeliveryAttempts.AnyAsync(row => row.AlertId == new AlertId(alertId))).Should().BeFalse();
        }

        var expectedKey = await fixture.ExpectedFirstAttemptKeyAsync(alertId);
        var early = Report("evt-race", "acs-e2e-0001", "Delivered", AzureCommunicationServicesSmsChannel.CreateTag(expectedKey));
        using var deferred = await fixture.PostReportsAsync(fixture.ValidToken(), early);
        deferred.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        deferred.Headers.RetryAfter.Should().NotBeNull();
        await using (var afterDeferral = fixture.CreateContext())
        {
            (await afterDeferral.InboxMessages.CountAsync()).Should().Be(0, "a deferred report must not be consumed");
            (await afterDeferral.DeliveryEvents.CountAsync(row => row.ProviderEventId == "acs-eg:evt-race")).Should().Be(0);
        }

        await fixture.ProcessAsync();
        using var redelivered = await fixture.PostReportsAsync(fixture.ValidToken(), early);
        redelivered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Delivered);

        var old = Report("evt-old-unknown", "acs-not-ours-0002", "Delivered", "ca-" + new string('2', 32));
        ((Dictionary<string, object>)old[0])["eventTime"] = DateTime.UtcNow.AddMinutes(-11).ToString("O");
        using var dropped = await fixture.PostReportsAsync(fixture.ValidToken(), old);
        dropped.StatusCode.Should().Be(HttpStatusCode.OK, "unmatched reports outside the deferral window belong elsewhere");
        fixture.Record("report-races-worker-commit", new
        {
            earlyReport = (int)deferred.StatusCode,
            redelivery = (int)redelivered.StatusCode,
            finalStatus = "Delivered",
            oldUnmatched = (int)dropped.StatusCode,
        });
    }

    [Fact]
    public async Task SubmittedAttemptStillClosesAfterTheDirectoryEntryIsDeactivated()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        await fixture.DeactivateMayaDirectoryEntryAsync();

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var waiting = await fixture.ProcessAsync();
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted,
            "a directory change after acceptance cannot fail or resend an accepted SMS");
        using (var delivered = await fixture.PostReportsAsync(fixture.ValidToken(),
                   Report("evt-after-directory-change", attempt.ProviderReference, "Delivered", AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey))))
            delivered.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var closed = await fixture.ProcessAsync();

        waiting.Outcome.Should().Be("rescheduled");
        closed.Outcome.Should().Be("processed");
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Delivered);
        fixture.Transport.Requests.Should().ContainSingle();
        fixture.Record("directory-change-after-accept", new { waiting = waiting.Outcome, afterReport = closed.Outcome, finalStatus = "Delivered", providerRequests = 1 });
    }

    [Fact]
    public async Task SubmittedAttemptWithoutAReportFailsVisiblyAfterTheDirectoryEntryIsDeactivated()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        await fixture.DeactivateMayaDirectoryEntryAsync();

        fixture.Clock.Advance(TimeSpan.FromSeconds(301));
        var result = await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue("the report window must still be evaluated instead of pending forever");
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("delivery-unconfirmed");
        fixture.Record("directory-change-no-report", new { outcome = result.Outcome, failureCategory = attempt.FailureCategory });
    }

    [Fact]
    public async Task SubmittedAttemptIsNeverEvaluatedByADifferentSmsProvider()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var result = await fixture.ProcessAsync(acsConfigured: false);

        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed, "the simulated provider never sent this SMS and cannot confirm it");
        attempt.FailureCategory.Should().Be("delivery-unconfirmed");
        attempt.DeliveredAtUtc.Should().BeNull();
        result.PermanentlyFailed.Should().BeTrue();
        fixture.Record("provider-changed-while-waiting", new { attemptStatus = "Failed", failureCategory = attempt.FailureCategory, delivered = false });
    }

    [Fact]
    public async Task CrashAfterAcceptanceReplaysTheSameRepeatableRequestAndKeepsTheOriginalMessageId()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        using var shutdown = new CancellationTokenSource();
        fixture.Transport.CrashAfterNextAccept(shutdown);
        var firstSendAt = fixture.Clock.GetUtcNow();

        var crashed = () => fixture.ProcessAsync(shutdown.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();
        await using (var afterCrash = fixture.CreateContext())
            (await afterCrash.DeliveryAttempts.CountAsync(row => row.AlertId == new AlertId(alertId))).Should().Be(0, "the dispatch transaction rolled back");

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var recovered = await fixture.ProcessAsync();

        var attempt = await fixture.SingleAttemptAsync(alertId);
        recovered.Outcome.Should().Be("rescheduled");
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        attempt.ProviderReference.Should().Be("acs-e2e-0001", "the replay must return the originally accepted message ID");
        fixture.Transport.Requests.Should().HaveCount(2);
        fixture.Transport.Requests.Select(row => row.RepeatabilityRequestId).Distinct().Should().ContainSingle();
        fixture.Transport.Requests.Select(row => row.RepeatabilityFirstSent).Distinct().Should().ContainSingle()
            .Which.Should().Be(firstSendAt.ToString("r"), "the first-send time was committed before the network call");
        fixture.Record("crash-after-accept-replay", new
        {
            providerRequests = 2,
            distinctRepeatabilityIds = 1,
            distinctFirstSent = 1,
            originalMessageIdKept = true,
            finalStatus = attempt.Status.ToString(),
        });
    }

    [Fact]
    public async Task FirstSendAfterAQueueBacklogUsesTheActualSendTimeAndIsAccepted()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        var sentAt = fixture.Clock.GetUtcNow();

        var result = await fixture.ProcessAsync();

        var attempt = await fixture.SingleAttemptAsync(alertId);
        result.Outcome.Should().Be("rescheduled");
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted, "a 10-minute backlog must not turn the first send into a 412 rejection");
        fixture.Transport.Requests.Should().ContainSingle().Which.RepeatabilityFirstSent.Should().Be(sentAt.ToString("r"));
        fixture.Record("first-send-after-backlog", new { backlogMinutes = 10, attemptStatus = attempt.Status.ToString(), firstSentIsActualSend = true });
    }

    [Fact]
    public async Task RecoveryAfterTheReplayWindowFailsVisiblyWithoutResending()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        using var shutdown = new CancellationTokenSource();
        fixture.Transport.CrashAfterNextAccept(shutdown);
        var crashed = () => fixture.ProcessAsync(shutdown.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();

        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        var recovered = await fixture.ProcessAsync();

        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("provider-outcome-uncertain", "ACS may already have sent it; a late replay is unsafe");
        fixture.Transport.Requests.Should().ContainSingle("no replay is sent outside the window");
        recovered.PermanentlyFailed.Should().BeTrue();
        fixture.Record("late-recovery-no-replay", new { minutesAfterFirstSend = 3, failureCategory = attempt.FailureCategory, providerRequests = 1 });
    }

    [Fact]
    public async Task BatchedReportsReloadTheAttemptAndNeverOverwriteATerminalStatusSetInBetween()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey);

        // One webhook request scope (one DbContext) applies two reports for the same attempt; between them the
        // worker makes the attempt terminal, exactly as a concurrent pass can while the batch is being processed.
        await using var webhookScope = fixture.CreateContext();
        var service = new ProviderDeliveryReportService(webhookScope, TimeProvider.System);
        var mismatch = await service.ApplyAsync(AzureCommunicationServicesSmsChannel.Provider,
            new ProviderDeliveryReport("evt-batch-1", attempt.ProviderReference, "ca-" + new string('3', 32), ProviderDeliveryReportStatus.Delivered, DateTimeOffset.UtcNow),
            Guid.NewGuid().ToString("N"), CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(301));
        (await fixture.ProcessAsync()).PermanentlyFailed.Should().BeTrue();
        var late = await service.ApplyAsync(AzureCommunicationServicesSmsChannel.Provider,
            new ProviderDeliveryReport("evt-batch-2", attempt.ProviderReference, tag, ProviderDeliveryReportStatus.Delivered, DateTimeOffset.UtcNow),
            Guid.NewGuid().ToString("N"), CancellationToken.None);

        mismatch.Should().Be(ProviderDeliveryReportOutcome.TagMismatch);
        late.Should().Be(ProviderDeliveryReportOutcome.NoStateChange);
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed, "the worker's terminal status must not be overwritten by a stale tracked copy");
        final.FailureCategory.Should().Be("delivery-unconfirmed");
        final.DeliveredAtUtc.Should().BeNull();
        fixture.Record("batched-reports-reload", new { first = mismatch.ToString(), second = late.ToString(), finalStatus = "Failed" });
    }

    [Fact]
    public async Task ThrottledReplayAfterALostAcceptedSendNeverCreatesASecondSms()
    {
        // F37: accepted-but-lost -> replay throttled (429) -> recovery must stay one logical send.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        fixture.Transport.LoseNextAcceptedResponse();

        await fixture.ProcessAsync();
        var afterLostResponse = await fixture.SingleAttemptAsync(alertId);
        fixture.Transport.Enqueue(HttpStatusCode.TooManyRequests);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.ProcessAsync();
        var afterThrottle = await fixture.SingleAttemptAsync(alertId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.ProcessAsync();

        await using var db = fixture.CreateContext();
        var attempts = await db.DeliveryAttempts.AsNoTracking().Where(row => row.AlertId == new AlertId(alertId)).ToArrayAsync();
        afterLostResponse.Status.Should().Be(DeliveryAttemptStatus.Requested);
        afterThrottle.Status.Should().Be(DeliveryAttemptStatus.Requested, "a throttled replay is not evidence the original was never accepted");
        attempts.Should().ContainSingle("no new attempt or request key may be created after an ambiguous send");
        attempts[0].Status.Should().Be(DeliveryAttemptStatus.Submitted);
        attempts[0].ProviderReference.Should().Be("acs-e2e-0001", "the replay recovers the originally accepted message");
        fixture.Transport.AcceptedSendCount.Should().Be(1, "one confirmed alert produces at most one accepted SMS");
        fixture.Transport.Requests.Should().HaveCount(3);
        fixture.Transport.Requests.Select(row => row.RepeatabilityRequestId).Distinct().Should().ContainSingle();
        fixture.Transport.Requests.Select(row => row.RepeatabilityFirstSent).Distinct().Should().ContainSingle();
        fixture.Record("lost-accept-then-throttled-replay", new
        {
            providerRequests = 3,
            acceptedSends = fixture.Transport.AcceptedSendCount,
            attempts = attempts.Length,
            distinctRepeatabilityIds = 1,
            finalStatus = attempts[0].Status.ToString(),
        });
    }

    [Fact]
    public async Task CrashThenRestartWithTheSimulatedProviderNeverClaimsSimulatedDelivery()
    {
        // F38: ACS accepted, the attempt row rolled back, the worker restarts with the default Simulation SMS.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        using var shutdown = new CancellationTokenSource();
        fixture.Transport.CrashAfterNextAccept(shutdown);
        var crashed = () => fixture.ProcessAsync(shutdown.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();

        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var restarted = await fixture.ProcessAsync(acsConfigured: false);

        await using var db = fixture.CreateContext();
        var attempt = await db.DeliveryAttempts.AsNoTracking().SingleAsync(row => row.AlertId == new AlertId(alertId));
        var events = await db.DeliveryEvents.AsNoTracking().Where(row => row.DeliveryAttemptId == attempt.Id).ToArrayAsync();
        attempt.Provider.Should().Be(AzureCommunicationServicesSmsChannel.Provider, "the attempt keeps the provider that actually sent it");
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("delivery-unconfirmed");
        attempt.DeliveredAtUtc.Should().BeNull();
        events.Should().NotContain(row => row.EventType == "delivered", "simulated delivery must never stand in for an ACS outcome");
        restarted.PermanentlyFailed.Should().BeTrue();
        fixture.Transport.Requests.Should().ContainSingle();
        fixture.Record("crash-then-simulation-restart", new
        {
            attemptProvider = "acs",
            attemptStatus = attempt.Status.ToString(),
            failureCategory = attempt.FailureCategory,
            simulatedDeliveryEvents = events.Count(row => row.EventType == "delivered"),
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatePrimaryFailureDoesNotDisableASuccessfulBackupWorkflow(bool backupAccepted)
    {
        // F39: primary SMS pending -> approved backup delivered (and accepted) -> primary times out.
        await fixture.ResetAsync(escalationStepDelay: TimeSpan.FromSeconds(1));
        await fixture.SeedRileyCurrentBackupOnCallAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        await fixture.RunEscalationUntilBackupQueuedAsync(alertId);
        fixture.SyncClockToRealTime();
        for (var pass = 0; pass < 4 && !await fixture.BackupDeliveredAsync(alertId); pass++)
            await fixture.ProcessAsync();
        (await fixture.BackupDeliveredAsync(alertId)).Should().BeTrue("the approved backup secure message is delivered");
        var version = (await fixture.SingleSmsAttemptAlertVersionAsync(alertId));
        using var riley = await fixture.SignedInAsync(DemoDataSeeder.RileyHandle);
        if (backupAccepted) await fixture.RespondAsync(riley, alertId, version, "Accepted");

        fixture.Clock.Advance(TimeSpan.FromSeconds(301));
        for (var pass = 0; pass < 3; pass++) await fixture.ProcessAsync();

        await using var db = fixture.CreateContext();
        var primary = await db.DeliveryAttempts.AsNoTracking().SingleAsync(row => row.AlertId == new AlertId(alertId) && row.Channel == NotificationChannel.Sms);
        var primaryOutbox = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.AggregateId == alertId && row.EventType == "AlertDispatchRequested");
        var alert = await db.Alerts.AsNoTracking().SingleAsync(row => row.Id == new AlertId(alertId));
        primary.Status.Should().Be(DeliveryAttemptStatus.Failed, "the primary channel failure stays visible");
        primary.FailureCategory.Should().Be("delivery-unconfirmed");
        primaryOutbox.ProcessingState.Should().Be(OutboxProcessingState.Failed);
        alert.State.Should().Be(AlertState.Active, "a successful backup workflow keeps the alert workable");
        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        live.GetProperty("operationalWarnings").EnumerateArray().Select(item => item.GetProperty("code").GetString()).Should().Contain("DeliveryFailed");
        if (!backupAccepted) await fixture.RespondAsync(riley, alertId, version, "Accepted");
        using var resolve = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alertId:D}/resolve")
        {
            Content = JsonContent.Create(new AlertLifecycleActionRequest(version)),
        };
        resolve.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var resolved = await operatorClient.SendAsync(resolve);
        resolved.StatusCode.Should().Be(HttpStatusCode.OK, await resolved.Content.ReadAsStringAsync());
        fixture.Record($"backup-success-then-primary-timeout-{(backupAccepted ? "accepted" : "unanswered")}", new
        {
            primaryAttempt = primary.Status.ToString(),
            primaryOutbox = primaryOutbox.ProcessingState.ToString(),
            alertStateAfterPrimaryTimeout = alert.State.ToString(),
            resolution = (int)resolved.StatusCode,
        });
    }

    [Fact]
    public async Task ReportPollsDoNotSpendTheWorkerFailureBudget()
    {
        // F41: send once, poll successfully several times, then one transient worker fault.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        for (var poll = 0; poll < 4; poll++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(6));
            (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var faulted = await fixture.ProcessAsync(transientWorkerFailure: true);
        var attempt = await fixture.SingleAttemptAsync(alertId);
        using (var delivered = await fixture.PostReportsAsync(fixture.ValidToken(),
                   Report("evt-after-fault", attempt.ProviderReference, "Delivered", AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey))))
            delivered.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var closed = await fixture.ProcessAsync();

        faulted.PermanentlyFailed.Should().BeFalse("one transient fault after successful polls is within the failure budget");
        faulted.Outcome.Should().Be("rescheduled");
        closed.Outcome.Should().Be("processed");
        await using var db = fixture.CreateContext();
        (await db.Alerts.AsNoTracking().SingleAsync(row => row.Id == new AlertId(alertId))).State.Should().Be(AlertState.Active);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Delivered);
        fixture.Record("polls-then-transient-fault", new { successfulPolls = 4, transientFaults = 1, faultOutcome = faulted.Outcome, finalOutcome = closed.Outcome });
    }

    [Fact]
    public async Task FirstSendTimeIsReadAtTheSendBoundaryNotWhenThePassStarted()
    {
        // F42: claim, lock wait and earlier recipients take longer than the 120-second uncertain window.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        var passStart = fixture.Clock.GetUtcNow();
        fixture.Clock.JumpAfterNextRead(TimeSpan.FromSeconds(121));

        await fixture.ProcessAsync();

        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted, "a never-sent recipient must be sent, not failed as uncertain");
        fixture.Transport.Requests.Should().ContainSingle().Which.RepeatabilityFirstSent
            .Should().Be(passStart.AddSeconds(121).ToString("r"), "the first-send time is the actual send boundary");
        // PostgreSQL stores microseconds; .NET keeps 100-ns ticks.
        attempt.RequestedAtUtc.Should().BeCloseTo(passStart.AddSeconds(121), TimeSpan.FromMilliseconds(1));
        fixture.Record("slow-path-to-first-send", new { secondsBeforeSend = 121, providerRequests = 1, attemptStatus = attempt.Status.ToString() });
    }

    [Fact]
    public async Task WebhookRejectsEveryUnauthenticatedOrMisauthorizedCaller()
    {
        await fixture.ResetAsync();
        var body = Report("evt-auth", "acs-e2e-0001", "Delivered", "ca-" + new string('1', 32));
        var results = new Dictionary<string, HttpStatusCode>();
        async Task Check(string name, string? token, HttpStatusCode expected, HttpClient? client = null)
        {
            using var response = await fixture.PostReportsAsync(token, body, client);
            response.StatusCode.Should().Be(expected, name);
            results[name] = response.StatusCode;
        }

        await Check("no-token", null, HttpStatusCode.Unauthorized);
        await Check("bad-signature", fixture.Token(signingKey: AcsSmsEndToEndFixture.CreateKey("attacker")), HttpStatusCode.Unauthorized);
        await Check("wrong-audience", fixture.Token(audience: "api://someone-else"), HttpStatusCode.Unauthorized);
        await Check("wrong-issuer", fixture.Token(issuer: "https://login.microsoftonline.com/99999999-9999-9999-9999-999999999999/v2.0"), HttpStatusCode.Unauthorized);
        await Check("expired", fixture.Token(expires: DateTime.UtcNow.AddMinutes(-10)), HttpStatusCode.Unauthorized);
        await Check("missing-role", fixture.Token(role: "SomethingElse"), HttpStatusCode.Forbidden);
        using (var cookieClient = await fixture.SignedInAsync(DemoDataSeeder.MorganHandle))
            await Check("development-cookie", null, HttpStatusCode.Unauthorized, cookieClient);

        // F40: same tenant, audience, signature, lifetime and role, but not the Microsoft.EventGrid principal.
        const string v1Issuer = "https://sts.windows.net/" + AcsSmsEndToEndFixture.TenantId + "/";
        await Check("subscription-writer-v1-appid", fixture.Token(issuer: v1Issuer, appId: AcsSmsEndToEndFixture.SubscriptionWriterAppId), HttpStatusCode.Forbidden);
        await Check("subscription-writer-v2-azp", fixture.Token(appId: null, authorizedParty: AcsSmsEndToEndFixture.SubscriptionWriterAppId), HttpStatusCode.Forbidden);
        await Check("missing-sender", fixture.Token(appId: null), HttpStatusCode.Forbidden);
        await Check("ambiguous-sender", fixture.Token(appId: AcsSmsEndToEndFixture.EventGridSenderAppId, authorizedParty: AcsSmsEndToEndFixture.SubscriptionWriterAppId), HttpStatusCode.Forbidden);
        await Check("reverse-ambiguous-sender", fixture.Token(appId: AcsSmsEndToEndFixture.SubscriptionWriterAppId, authorizedParty: AcsSmsEndToEndFixture.EventGridSenderAppId), HttpStatusCode.Forbidden);
        await Check("empty-sender", fixture.Token(appId: ""), HttpStatusCode.Forbidden);
        await Check("malformed-sender", fixture.Token(appId: "sim-event-grid"), HttpStatusCode.Forbidden);
        using (var forgedHandshake = await fixture.PostReportsAsync(
                   fixture.Token(appId: AcsSmsEndToEndFixture.SubscriptionWriterAppId),
                   new[] { new { id = "evt-forged-handshake", topic = "/subscriptions/x", subject = "", eventType = "Microsoft.EventGrid.SubscriptionValidationEvent",
                       eventTime = DateTime.UtcNow.ToString("O"), dataVersion = "1", data = new { validationCode = "SIM-FORGED-CODE-0001" } } },
                   eventTypeHeader: "SubscriptionValidation"))
        {
            forgedHandshake.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await forgedHandshake.Content.ReadAsStringAsync()).Should().NotContain("SIM-FORGED-CODE-0001");
            results["subscription-writer-handshake"] = forgedHandshake.StatusCode;
        }

        // The configured sender is authorized through either token version (an unmatched recent report defers).
        await Check("event-grid-v1-appid", fixture.Token(issuer: v1Issuer), HttpStatusCode.ServiceUnavailable);
        await Check("event-grid-v2-azp", fixture.Token(appId: null, authorizedParty: AcsSmsEndToEndFixture.EventGridSenderAppId), HttpStatusCode.ServiceUnavailable);

        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0);
        (await db.AuditEvents.CountAsync(row => row.ActorType == "provider-webhook")).Should().Be(0);
        fixture.Record("webhook-authentication", results.ToDictionary(item => item.Key, item => (int)item.Value));
    }

    [Fact]
    public async Task RepeatedWorkerFaultsStillExhaustTheFailureBudgetAfterReportPolls()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var firstFault = await fixture.ProcessAsync(transientWorkerFailure: true);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var secondFault = await fixture.ProcessAsync(transientWorkerFailure: true);

        firstFault.Outcome.Should().Be("rescheduled");
        secondFault.PermanentlyFailed.Should().BeTrue();
        await using var db = fixture.CreateContext();
        (await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.AggregateId == alertId))
            .ProcessingState.Should().Be(OutboxProcessingState.Failed);
        fixture.Transport.Requests.Should().ContainSingle("report polls and worker faults never resend a submitted SMS");
        fixture.Record("bounded-worker-failures-after-poll", new { transientFaults = 2, firstFault = firstFault.Outcome, secondFault = secondFault.Outcome });
    }

    [Fact]
    public async Task SubscriptionWriterCannotForgeTheStateOfASubmittedSms()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey);
        var forged = Report("evt-forged-delivery", attempt.ProviderReference, "Delivered", tag);
        var tokens = new[]
        {
            fixture.Token(appId: AcsSmsEndToEndFixture.SubscriptionWriterAppId),
            fixture.Token(appId: null, authorizedParty: AcsSmsEndToEndFixture.SubscriptionWriterAppId),
        };
        foreach (var token in tokens)
        {
            using var rejected = await fixture.PostReportsAsync(token, forged);
            rejected.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        await using (var db = fixture.CreateContext())
        {
            (await db.InboxMessages.CountAsync()).Should().Be(0);
            (await db.DeliveryEvents.CountAsync(row => row.ProviderEventId == "acs-eg:evt-forged-delivery")).Should().Be(0);
            (await db.AuditEvents.CountAsync(row => row.ActorType == "provider-webhook")).Should().Be(0);
        }

        using var legitimate = await fixture.PostReportsAsync(
            fixture.Token(appId: null, authorizedParty: AcsSmsEndToEndFixture.EventGridSenderAppId), forged);
        legitimate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Delivered);
        fixture.Record("writer-cannot-forge-delivery", new { writerRequests = 2, writerStatus = 403, eventGridStatus = 200, finalStatus = "Delivered" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sim-event-grid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void EnabledWebhookRefusesMissingOrInvalidSenderApplicationId(string? senderApplicationId)
    {
        using var invalid = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Communications:Webhooks:EventGrid:Enabled", "true");
            builder.UseSetting("Communications:Webhooks:EventGrid:TenantId", AcsSmsEndToEndFixture.TenantId);
            builder.UseSetting("Communications:Webhooks:EventGrid:Audience", AcsSmsEndToEndFixture.Audience);
            builder.UseSetting("Communications:Webhooks:EventGrid:ExpectedTopic", AcsSmsEndToEndFixture.Topic);
            builder.UseSetting("Communications:Webhooks:EventGrid:SubscriptionName", AcsSmsEndToEndFixture.SubscriptionName);
            builder.UseSetting("Communications:Webhooks:EventGrid:SenderApplicationId", senderApplicationId);
        });
        FluentActions.Invoking(() => invalid.CreateClient()).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("SenderApplicationId");
    }

    [Fact]
    public async Task ConfiguredSenderGuidAcceptsCanonicalClaimsAfterNormalization()
    {
        await fixture.ResetAsync();
        var body = Report("evt-normalized-sender", "acs-e2e-0001", "Delivered", "ca-" + new string('1', 32));
        using var response = await fixture.PostReportsWithSenderApplicationIdAsync(
            " " + AcsSmsEndToEndFixture.EventGridSenderAppId.ToUpperInvariant() + " ", body);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "the configured GUID must authorize the same canonical sender claim before report processing");
        fixture.Record("normalized-sender-configuration", new { authorizedReport = 503 });
    }

    [Fact]
    public async Task WebhookValidatesTheWholeBatchBeforeApplyingAnything()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = AzureCommunicationServicesSmsChannel.CreateTag(attempt.IdempotencyKey);
        var good = ReportEvent("evt-good", attempt.ProviderReference, "Delivered", tag);
        var results = new Dictionary<string, int>();
        async Task Check(string name, HttpContent content, HttpStatusCode expected,
            string subscriptionName = AcsSmsEndToEndFixture.SubscriptionName, string eventTypeHeader = "Notification")
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AcsSmsEndToEndFixture.WebhookPath) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ValidToken());
            request.Headers.Add("aeg-subscription-name", subscriptionName);
            request.Headers.Add("aeg-event-type", eventTypeHeader);
            using var response = await fixture.Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(expected, name);
            text.Should().NotContain(AcsSmsEndToEndFixture.TestNumber);
            results[name] = (int)response.StatusCode;
        }

        await Check("content-type", new StringContent(JsonSerializer.Serialize(new[] { good }), Encoding.UTF8, "text/plain"), HttpStatusCode.UnsupportedMediaType);
        await Check("oversize", Json(new string(' ', 70 * 1024) + "[]"), HttpStatusCode.RequestEntityTooLarge);
        await Check("too-many-events", Json(Enumerable.Range(0, 51).Select(index => ReportEvent($"evt-{index}", attempt.ProviderReference, "Delivered", tag)).ToArray()), HttpStatusCode.BadRequest);
        await Check("unsupported-type", Json(new[] { good, With(good, "eventType", "Microsoft.Communication.SMSReceived", "evt-type") }), HttpStatusCode.BadRequest);
        await Check("wrong-topic", Json(new[] { good, With(good, "topic", "/subscriptions/x/providers/microsoft.communication/communicationservices/other", "evt-topic") }), HttpStatusCode.BadRequest);
        await Check("stale", Json(new[] { good, With(good, "eventTime", DateTime.UtcNow.AddDays(-3).ToString("O"), "evt-stale") }), HttpStatusCode.BadRequest);
        await Check("future", Json(new[] { good, With(good, "eventTime", DateTime.UtcNow.AddHours(1).ToString("O"), "evt-future") }), HttpStatusCode.BadRequest);
        await Check("unknown-status", Json(new[] { good, ReportEvent("evt-status", attempt.ProviderReference, "Expired", tag) }), HttpStatusCode.BadRequest);
        await Check("unsafe-message-id", Json(new[] { good, ReportEvent("evt-unsafe", "id with spaces", "Delivered", tag) }), HttpStatusCode.BadRequest);
        await Check("duplicate-id-in-batch", Json(new[] { good, good }), HttpStatusCode.BadRequest);
        await Check("not-json", new StringContent("{nope", Encoding.UTF8, "application/json"), HttpStatusCode.BadRequest);
        await Check("unintended-subscription", Json(new[] { good }), HttpStatusCode.BadRequest, subscriptionName: "SIM-UNINTENDED-SUBSCRIPTION");
        await Check("handshake-header-on-report", Json(new[] { good }), HttpStatusCode.BadRequest, eventTypeHeader: "SubscriptionValidation");

        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0, "no event of a rejected batch may be applied");
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Record("webhook-validation", results);
    }

    [Fact]
    public async Task SubscriptionValidationEchoesTheCodeOnlyForTheConfiguredSubscriptionWhenAuthenticated()
    {
        // Shape of Microsoft's documented handshake: the topic is a bare subscription path, not the ACS
        // resource or the system topic, so the intended subscription is recognized by aeg-subscription-name.
        static object[] Validation(DateTime eventTime, string topic = "/subscriptions/00000000-0000-0000-0000-000000000000", bool withId = true)
        {
            var item = new Dictionary<string, object>
            {
                ["topic"] = topic,
                ["subject"] = string.Empty,
                ["eventType"] = "Microsoft.EventGrid.SubscriptionValidationEvent",
                ["eventTime"] = eventTime.ToString("O"),
                ["metadataVersion"] = "1",
                ["dataVersion"] = "1",
                ["data"] = new
                {
                    validationCode = "SIM-VALIDATION-CODE-0001",
                    validationUrl = "https://sim-eventgrid.invalid:553/eventsubscriptions/sim/validate?id=SIM",
                },
            };
            if (withId) item["id"] = "0f2c3c58-6a6e-4f0e-9b0b-5d7b9a3d0001";
            return [item];
        }

        const string handshake = "SubscriptionValidation";
        var intended = Validation(DateTime.UtcNow);
        using var anonymous = await fixture.PostReportsAsync(null, intended, eventTypeHeader: handshake);
        using var authenticated = await fixture.PostReportsAsync(fixture.ValidToken(), intended, eventTypeHeader: handshake);
        var echoed = await authenticated.Content.ReadFromJsonAsync<JsonElement>();
        using var lowerCaseName = await fixture.PostReportsAsync(fixture.ValidToken(), intended, eventTypeHeader: handshake,
            subscriptionName: AcsSmsEndToEndFixture.SubscriptionName.ToLowerInvariant());
        using var acsResourceTopic = await fixture.PostReportsAsync(fixture.ValidToken(), Validation(DateTime.UtcNow, AcsSmsEndToEndFixture.Topic), eventTypeHeader: handshake);
        using var otherSubscription = await fixture.PostReportsAsync(fixture.ValidToken(), intended, eventTypeHeader: handshake, subscriptionName: "SIM-UNINTENDED-SUBSCRIPTION");
        using var noSubscription = await fixture.PostReportsAsync(fixture.ValidToken(), intended, eventTypeHeader: handshake, subscriptionName: null);
        using var wrongEventHeader = await fixture.PostReportsAsync(fixture.ValidToken(), intended, eventTypeHeader: "Notification");
        using var stale = await fixture.PostReportsAsync(fixture.ValidToken(), Validation(DateTime.UtcNow.AddDays(-3)), eventTypeHeader: handshake);
        using var missingId = await fixture.PostReportsAsync(fixture.ValidToken(), Validation(DateTime.UtcNow, withId: false), eventTypeHeader: handshake);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        echoed.GetProperty("validationResponse").GetString().Should().Be("SIM-VALIDATION-CODE-0001");
        lowerCaseName.StatusCode.Should().Be(HttpStatusCode.OK, "Event Grid upper-cases subscription names");
        acsResourceTopic.StatusCode.Should().Be(HttpStatusCode.OK, "the handshake topic is not used to recognize the subscription");
        foreach (var rejected in new[] { otherSubscription, noSubscription, wrongEventHeader, stale, missingId })
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await rejected.Content.ReadAsStringAsync()).Should().NotContain("SIM-VALIDATION-CODE-0001");
        }

        fixture.Record("subscription-validation", new
        {
            anonymous = (int)anonymous.StatusCode,
            authenticated = (int)authenticated.StatusCode,
            subscriptionNameCaseInsensitive = (int)lowerCaseName.StatusCode,
            anyHandshakeTopic = (int)acsResourceTopic.StatusCode,
            unintendedSubscription = (int)otherSubscription.StatusCode,
            missingSubscriptionHeader = (int)noSubscription.StatusCode,
            wrongEventTypeHeader = (int)wrongEventHeader.StatusCode,
            stale = (int)stale.StatusCode,
            missingId = (int)missingId.StatusCode,
        });
    }

    [Fact]
    public async Task AmbiguousProviderOutcomeIsRetriedWithTheSameRepeatabilityKeyAndOneAttempt()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);
        fixture.Transport.Enqueue(HttpStatusCode.InternalServerError);

        var first = await fixture.ProcessAsync();
        var pending = await fixture.SingleAttemptAsync(alertId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var second = await fixture.ProcessAsync();
        var submitted = await fixture.SingleAttemptAsync(alertId);

        first.Outcome.Should().Be("rescheduled");
        pending.Status.Should().Be(DeliveryAttemptStatus.Requested);
        pending.ProviderReference.Should().BeEmpty();
        second.Outcome.Should().Be("rescheduled");
        submitted.Id.Should().Be(pending.Id);
        submitted.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Transport.Requests.Should().HaveCount(2);
        fixture.Transport.Requests.Select(row => row.RepeatabilityRequestId).Distinct().Should().ContainSingle();
        fixture.Transport.Requests.Select(row => row.RepeatabilityFirstSent).Distinct().Should().ContainSingle();
        fixture.Record("uncertain-outcome-same-key", new
        {
            attempts = 1,
            providerRequests = fixture.Transport.Requests.Count,
            distinctRepeatabilityIds = fixture.Transport.Requests.Select(row => row.RepeatabilityRequestId).Distinct().Count(),
            finalStatus = submitted.Status.ToString(),
        });
    }

    [Fact]
    public async Task MissingDeliveryReportFailsVisiblyAsUnconfirmedAfterTheDemoWindow()
    {
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedSmsAlertAsync(operatorClient);

        await fixture.ProcessAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(301));
        var result = await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("delivery-unconfirmed");
        fixture.Transport.Requests.Should().ContainSingle("waiting for a report never resends");
        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        live.GetProperty("alertState").GetString().Should().Be("Failed");
        live.GetProperty("recipients")[0].GetProperty("attempts")[0].GetProperty("failureCategory").GetString().Should().Be("delivery-unconfirmed");
        live.GetProperty("operationalWarnings").EnumerateArray().Select(item => item.GetProperty("code").GetString())
            .Should().Contain("DeliveryFailed");
        fixture.Record("delivery-unconfirmed", new { attemptStatus = "Failed", failureCategory = attempt.FailureCategory, alertState = "Failed" });
    }

    [Fact]
    public async Task ProviderSelectionDefaultsToSimulationAndFailsClosedWhenMisconfigured()
    {
        static IReadOnlyList<INotificationChannel> Channels(IDictionary<string, string?> settings, string environment)
        {
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddLogging();
            services.AddSimulationDispatch();
            services.AddConfiguredSmsProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), environment);
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            return scope.ServiceProvider.GetServices<INotificationChannel>().ToArray();
        }

        Channels(new Dictionary<string, string?>(), "Test")
            .Single(channel => channel.ChannelType == NotificationChannel.Sms).Should().BeOfType<SimulationSmsChannel>();
        Channels(AcsSmsEndToEndFixture.AcsSettings(), "Test")
            .Single(channel => channel.ChannelType == NotificationChannel.Sms).Should().BeOfType<AzureCommunicationServicesSmsChannel>();
        FluentActions.Invoking(() => Channels(AcsSmsEndToEndFixture.AcsSettings(), "Production"))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("REQUIRES_HOSPITAL_DECISION");

        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Communications:Webhooks:EventGrid:Enabled", "true");
            builder.UseSetting("Communications:Webhooks:EventGrid:TenantId", AcsSmsEndToEndFixture.TenantId);
            builder.UseSetting("Communications:Webhooks:EventGrid:Audience", AcsSmsEndToEndFixture.Audience);
            builder.UseSetting("Communications:Webhooks:EventGrid:ExpectedTopic", AcsSmsEndToEndFixture.Topic);
            builder.UseSetting("Communications:Webhooks:EventGrid:SubscriptionName", AcsSmsEndToEndFixture.SubscriptionName);
        });
        FluentActions.Invoking(() => production.CreateClient()).Should().Throw<InvalidOperationException>();

        using var disabled = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        });
        using var client = disabled.CreateClient();
        using var response = await client.PostAsync(AcsSmsEndToEndFixture.WebhookPath, new StringContent("[]", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.Record("provider-selection", new { defaultChannel = "SimulationSmsChannel", configuredChannel = "AzureCommunicationServicesSmsChannel", productionRefused = true, webhookAbsentByDefault = true });
    }

    private static object[] Report(string id, string messageId, string status, string tag) => [ReportEvent(id, messageId, status, tag)];

    private static Dictionary<string, object> ReportEvent(string id, string messageId, string status, string tag) => new()
    {
        ["id"] = id,
        ["topic"] = AcsSmsEndToEndFixture.Topic,
        ["subject"] = "/phonenumber/15555550142",
        ["eventType"] = "Microsoft.Communication.SMSDeliveryReportReceived",
        ["eventTime"] = DateTime.UtcNow.ToString("O"),
        ["dataVersion"] = "1.0",
        ["metadataVersion"] = "1",
        ["data"] = new Dictionary<string, object>
        {
            ["messageId"] = messageId,
            ["from"] = "18005550100",
            ["to"] = AcsSmsEndToEndFixture.TestNumber,
            ["deliveryStatus"] = status,
            ["deliveryStatusDetails"] = "SIMULATION provider detail " + AcsSmsEndToEndFixture.TestNumber,
            ["receivedTimestamp"] = DateTime.UtcNow.ToString("O"),
            ["Tag"] = tag,
        },
    };

    private static Dictionary<string, object> With(Dictionary<string, object> source, string key, object value, string id)
        => new(source) { [key] = value, ["id"] = id };

    private static StringContent Json(object value)
        => new(value as string ?? JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}

public sealed class AcsSmsEndToEndFixture : IAsyncLifetime
{
    public const string TenantId = "0f0f0f0f-1111-4222-8333-444444444444";
    public const string Audience = "api://sim-critical-alerts-webhook";
    public const string Topic = "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/sim-rg/providers/Microsoft.Communication/CommunicationServices/sim-critical-alerts";
    public const string SubscriptionName = "SIM-CRITICAL-ALERTS-SMS-REPORTS";
    /// <summary>Fictional stand-in for the Microsoft.EventGrid application ID (read from the tenant in real use).</summary>
    public const string EventGridSenderAppId = "5ee5ee5e-0000-4000-8000-00000000e9e9";
    /// <summary>Fictional subscription-writer app that legitimately holds the same app role.</summary>
    public const string SubscriptionWriterAppId = "0bad0bad-0000-4000-8000-000000000001";
    public const string TestNumber = "+15555550142";
    public const string WebhookPath = "/api/v1/webhooks/communications/acs-sms";
    public static readonly string AccessKey = Convert.ToBase64String(Enumerable.Range(40, 32).Select(value => (byte)value).ToArray());
    private static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    private static readonly RsaSecurityKey SigningKey = CreateKey("event-grid-test");

    private readonly PostgresApiFixture inner = new();
    private readonly CapturingLoggerProvider logs = new();
    private readonly ConcurrentDictionary<string, object> evidence = new();
    private readonly List<string> issuedTokens = [];
    private string dataProtectionKey = string.Empty;
    private WebApplicationFactory<Program>? factory;

    public MutableClock Clock { get; private set; } = new(DateTimeOffset.UtcNow);

    public SigningFakeAcs Transport { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public static Dictionary<string, string?> AcsSettings() => new()
    {
        ["Communications:Sms:Provider"] = "AzureCommunicationServices",
        ["Communications:Sms:AzureCommunicationServices:Endpoint"] = "https://sim-critical-alerts.communication.azure.com",
        ["Communications:Sms:AzureCommunicationServices:AccessKey"] = AccessKey,
        ["Communications:Sms:AzureCommunicationServices:FromNumber"] = "+18005550100",
        ["Communications:Sms:AzureCommunicationServices:TestRecipients:SIM-SMS-0101"] = TestNumber,
    };

    public static RsaSecurityKey CreateKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    public async Task InitializeAsync()
    {
        await inner.InitializeAsync();
        Transport = new SigningFakeAcs(AccessKey, () => Clock);
        dataProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", inner.ConnectionString);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
            builder.UseSetting("SimulationResponses:Enabled", "true");
            builder.UseSetting("DataProtection:Key", dataProtectionKey);
            builder.UseSetting("Communications:Webhooks:EventGrid:Enabled", "true");
            builder.UseSetting("Communications:Webhooks:EventGrid:TenantId", TenantId);
            builder.UseSetting("Communications:Webhooks:EventGrid:Audience", Audience);
            builder.UseSetting("Communications:Webhooks:EventGrid:ExpectedTopic", Topic);
            builder.UseSetting("Communications:Webhooks:EventGrid:SubscriptionName", SubscriptionName);
            builder.UseSetting("Communications:Webhooks:EventGrid:SenderApplicationId", EventGridSenderAppId);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.Configure<RateLimiterOptions>(options =>
                {
                    options.AddPolicy("api", _ => RateLimitPartition.GetNoLimiter("phase12-e2e"));
                    options.AddPolicy("webhook", _ => RateLimitPartition.GetNoLimiter("phase12-e2e-webhook"));
                });
                // Replaces tenant metadata discovery with a fixed fictional signing key; all other validation is production code.
                services.PostConfigure<JwtBearerOptions>("EventGridWebhook", options =>
                {
                    var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    configuration.SigningKeys.Add(SigningKey);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                });
            });
        });
        factory.Server.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        Client = CreateClient();
    }

    public async Task DisposeAsync()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "phase12");
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is not null) directory = Path.Combine(repositoryRoot, "TestResults", "phase12");
        Directory.CreateDirectory(directory);
        var artifact = JsonSerializer.Serialize(new
        {
            suite = "phase12-acs-sms-e2e",
            simulationOnly = true,
            liveProviderCalled = false,
            scenarios = evidence.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(item => item.Key, item => item.Value),
        }, new JsonSerializerOptions { WriteIndented = true });
        foreach (var forbidden in ForbiddenValues())
            if (artifact.Contains(forbidden, StringComparison.Ordinal))
                throw new InvalidOperationException("The Phase 12 evidence artifact contains a sensitive value.");
        await File.WriteAllTextAsync(Path.Combine(directory, "acs-sms-e2e-evidence.json"), artifact);
        Client.Dispose();
        factory?.Dispose();
        await inner.DisposeAsync();
    }

    public void Record(string scenario, object result) => evidence[scenario] = result;

    public CriticalAlertsDbContext CreateContext() => DatabaseOperations.CreateContext(inner.ConnectionString);

    public HttpClient CreateClient()
        => factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public async Task ResetAsync(TimeSpan? escalationStepDelay = null)
    {
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true,
            escalationStepDelay: escalationStepDelay);
        Clock = new MutableClock(DateTimeOffset.UtcNow);
        Transport = new SigningFakeAcs(AccessKey, () => Clock);
    }

    public async Task<HttpClient> SignedInAsync(string handle)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = handle });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public string ValidToken() => Token();

    public string Token(
        string? issuer = null,
        string? audience = null,
        string role = "AzureEventGridSecureWebhookSubscriber",
        DateTime? expires = null,
        RsaSecurityKey? signingKey = null,
        string? appId = EventGridSenderAppId,
        string? authorizedParty = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
        var claims = new Dictionary<string, object> { ["roles"] = new[] { role } };
        if (appId is not null) claims["appid"] = appId;
        if (authorizedParty is not null) claims["azp"] = authorizedParty;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            IssuedAt = expiry.AddMinutes(-20),
            NotBefore = expiry.AddMinutes(-20),
            Expires = expiry,
            Claims = claims,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
        lock (issuedTokens) issuedTokens.Add(token);
        return token;
    }

    /// <summary>Posts like Event Grid: bearer token plus the aeg-subscription-name and aeg-event-type delivery headers.</summary>
    public async Task<HttpResponseMessage> PostReportsAsync(
        string? token,
        object events,
        HttpClient? client = null,
        string eventTypeHeader = "Notification",
        string? subscriptionName = SubscriptionName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(events), Encoding.UTF8, "application/json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (subscriptionName is not null) request.Headers.Add("aeg-subscription-name", subscriptionName);
        request.Headers.Add("aeg-event-type", eventTypeHeader);
        return await (client ?? Client).SendAsync(request);
    }

    public async Task<HttpResponseMessage> PostReportsWithSenderApplicationIdAsync(string senderApplicationId, object events)
    {
        using var configured = factory!.WithWebHostBuilder(builder =>
            builder.UseSetting("Communications:Webhooks:EventGrid:SenderApplicationId", senderApplicationId));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return await PostReportsAsync(ValidToken(), events, client);
    }

    public async Task<Guid> CreateConfirmedSmsAlertAsync(HttpClient client)
    {
        using var create = await client.PostAsJsonAsync("/api/v1/alerts/drafts", new CreateAlertDraftRequest(
            DemoDataSeeder.NorthSiteId.Value,
            DemoDataSeeder.EmergencyDepartmentId.Value,
            "SIM-PAT-PHASE12-0001",
            "North Wing / Simulation Room 204",
            "Urgent",
            "SIMULATION: phase twelve sms source",
            new AlertSbarDraft("SIMULATION: s", "SIMULATION: b", "SIMULATION: a", "SIMULATION: r"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var draft = (await create.Content.ReadFromJsonAsync<AlertDraftView>())!;
        using var approved = await client.PutAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, "SIMULATION: phase twelve approved message"));
        approved.EnsureSuccessStatusCode();
        var approvedDraft = (await approved.Content.ReadFromJsonAsync<AlertDraftView>())!;
        var maya = (await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/v1/directory/practitioners?q=Maya&includeInactive=false"))!.Single();
        using var recipients = await client.PutAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(approvedDraft.DraftVersion,
                [new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "Sms", maya.SelectionRevision)]));
        recipients.StatusCode.Should().Be(HttpStatusCode.OK, await recipients.Content.ReadAsStringAsync());
        var recipientDraft = (await recipients.Content.ReadFromJsonAsync<AlertDraftView>())!;
        using var field = await client.PostAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(recipientDraft.DraftVersion, "heartRate", "118", "118", "beats/min"));
        field.EnsureSuccessStatusCode();
        var confirmedDraft = (await field.Content.ReadFromJsonAsync<AlertDraftView>())!;
        using var submit = await client.PostAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmedDraft.DraftVersion));
        submit.EnsureSuccessStatusCode();
        var review = (await client.GetFromJsonAsync<AlertReviewView>($"/api/v1/alerts/{draft.AlertId:D}/review"))!;
        using var confirm = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{draft.AlertId:D}/confirm")
        {
            Content = JsonContent.Create(new ConfirmAlertReviewRequest(confirmedDraft.DraftVersion,
                review.EscalationPlan!.PolicyId, review.EscalationPlan.PolicyVersion, review.EscalationPlan.Revision)),
        };
        confirm.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var confirmed = await client.SendAsync(confirm);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK, await confirmed.Content.ReadAsStringAsync());
        // The worker clock starts after the API committed the outbox row at real time.
        Clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(1));
        return draft.AlertId;
    }

    /// <summary>One worker pass. <paramref name="acsConfigured"/> false models a worker restarted with the simulated SMS provider.</summary>
    public async Task<DispatchProcessingResult> ProcessAsync(
        CancellationToken cancellationToken = default,
        bool acsConfigured = true,
        bool transientWorkerFailure = false)
    {
        await using var db = CreateContext();
        // Not disposed: the shared fake transport outlives each worker pass, like a pooled handler.
        INotificationChannel sms = acsConfigured
            ? new AzureCommunicationServicesSmsChannel(
                AcsSmsOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(AcsSettings()).Build(), "Test")!,
                Transport,
                Clock)
            : new SimulationSmsChannel(Clock);
        if (transientWorkerFailure) sms = new TransientlyFailingChannel(sms);
        var processor = new OutboxDispatchProcessor(
            db,
            [new SimulationSecureMessageChannel(Clock), sms, new SimulationVoiceChannel(Clock)],
            new SimulationDeliveryEventNormalizer(),
            new SimulationDispatchScenarioStore(db),
            Clock,
            Options.Create(new DispatchWorkerOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                MaxAttempts = 2,
                RetryDelay = TimeSpan.FromSeconds(5),
            }),
            NullLogger<OutboxDispatchProcessor>.Instance);
        return await processor.ProcessNextAsync("phase12-e2e-worker", cancellationToken);
    }

    /// <summary>Directory sync outcome after acceptance: the endpoint is removed and the practitioner deactivated.</summary>
    public async Task DeactivateMayaDirectoryEntryAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contact_endpoints SET is_active = false WHERE practitioner_id = {DemoDataSeeder.MayaChenId.Value}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE practitioners SET is_active = false WHERE id = {DemoDataSeeder.MayaChenId.Value}");
    }

    /// <summary>
    /// Makes fictional Riley a current backup on-call for the emergency department, so the review offers a
    /// reviewed backup step (the seeded on-call windows are historical).
    /// </summary>
    public async Task SeedRileyCurrentBackupOnCallAsync()
    {
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        await using var db = CreateContext();
        db.PractitionerRoles.Add(Domain.Directory.PractitionerRoleAssignment.Create(PractitionerRoleId.New(),
            DemoDataSeeder.OrganizationId, DemoDataSeeder.RileySatoId, DemoDataSeeder.EmergencyDepartmentId,
            "Fictional emergency cover", false, "SIM-DIRECTORY", "SIM-ROLE-RILEY-PHASE12"));
        db.OnCallAssignments.Add(Domain.Directory.OnCallAssignment.Create(OnCallAssignmentId.New(), DemoDataSeeder.OrganizationId,
            DemoDataSeeder.RileySatoId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            OnCallTier.Backup, now.AddHours(-2), now.AddHours(2), "SIM-ROSTER", "SIM-PHASE12-BACKUP", now.AddMinutes(-5)));
        await db.SaveChangesAsync();
    }

    /// <summary>Runs the real escalation processor (database clock) until the backup dispatch is queued.</summary>
    public async Task RunEscalationUntilBackupQueuedAsync(Guid alertId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var db = CreateContext())
            {
                await new EscalationProcessor(db).ProcessNextAsync("phase12-e2e-escalation");
                if (await db.OutboxMessages.AnyAsync(row => row.AggregateId == alertId && row.EventType == "EscalationDispatchRequested"))
                    return;
            }

            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("The DEMO escalation step did not queue a backup dispatch.");
            await Task.Delay(250);
        }
    }

    public async Task<bool> BackupDeliveredAsync(Guid alertId)
    {
        await using var db = CreateContext();
        return await db.DeliveryAttempts.AsNoTracking().AnyAsync(row => row.AlertId == new AlertId(alertId)
            && row.Channel == NotificationChannel.SecureMessage && row.Status == DeliveryAttemptStatus.Delivered);
    }

    public async Task<int> SingleSmsAttemptAlertVersionAsync(Guid alertId)
    {
        await using var db = CreateContext();
        return (await db.Alerts.AsNoTracking().SingleAsync(row => row.Id == new AlertId(alertId))).DraftVersion.Value;
    }

    /// <summary>A practitioner's explicit response through the authenticated API.</summary>
    public async Task RespondAsync(HttpClient practitioner, Guid alertId, int version, string responseType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/my-alerts/{alertId:D}/responses")
        {
            Content = JsonContent.Create(new { expectedVersion = version, responseType }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await practitioner.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Moves the worker clock to real time so rows written with the database clock become due.</summary>
    public void SyncClockToRealTime() => Clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(1));

    /// <summary>The worker's stable first-attempt key (alert, confirmed version, recipient, channel, attempt 1).</summary>
    public async Task<string> ExpectedFirstAttemptKeyAsync(Guid alertId)
    {
        await using var db = CreateContext();
        var alert = await db.Alerts.Include(row => row.RecipientSelections).AsNoTracking().SingleAsync(row => row.Id == new AlertId(alertId));
        var recipient = alert.CurrentRecipients.Single();
        return $"alert-dispatch:{alertId:N}:v{alert.DraftVersion.Value}:r{recipient.Id.Value:N}:c{(int)recipient.Channel}:a1";
    }

    public async Task<Domain.Delivery.DeliveryAttempt> SingleAttemptAsync(Guid alertId)
    {
        await using var db = CreateContext();
        return await db.DeliveryAttempts.AsNoTracking().SingleAsync(row => row.AlertId == new AlertId(alertId));
    }

    public async Task AssertNoSensitiveValuesPersistedOrLoggedAsync()
    {
        await using var db = CreateContext();
        var persisted = new List<string>();
        persisted.AddRange(await db.DeliveryAttempts.Select(row => row.ProviderReference + "|" + row.FailureCategory + "|" + row.IdempotencyKey).ToArrayAsync());
        persisted.AddRange(await db.DeliveryEvents.Select(row => row.ProviderEventId + "|" + row.SanitizedMetadata + "|" + row.EventType).ToArrayAsync());
        persisted.AddRange(await db.InboxMessages.Select(row => row.ExternalMessageId + "|" + row.Result).ToArrayAsync());
        persisted.AddRange((await db.AuditEvents.Select(row => new { row.SanitizedMetadata, row.CorrelationId }).ToArrayAsync())
            .Select(row => row.SanitizedMetadata + "|" + row.CorrelationId));
        persisted.AddRange((await db.OutboxMessages.Select(row => new { row.PayloadJson, row.LastErrorCategory }).ToArrayAsync())
            .Select(row => row.PayloadJson + "|" + row.LastErrorCategory));
        foreach (var value in ForbiddenValues())
        {
            persisted.Should().NotContain(row => row.Contains(value, StringComparison.Ordinal), "persisted operational rows must not contain {0}", "a sensitive value");
            logs.Entries.Should().NotContain(row => row.Contains(value, StringComparison.Ordinal));
        }
    }

    private IEnumerable<string> ForbiddenValues()
    {
        yield return TestNumber;
        yield return TestNumber[1..];
        yield return "15555550142";
        yield return AccessKey;
        yield return "SIMULATION provider detail";
        yield return "SIMULATION: please open the secure alert application.";
        lock (issuedTokens)
            foreach (var token in issuedTokens) yield return token;
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        return null;
    }
}

[CollectionDefinition(Name)]
public sealed class AcsSmsEndToEndCollection : ICollectionFixture<AcsSmsEndToEndFixture>
{
    public const string Name = "phase12-acs-sms-e2e";
}

/// <summary>A recoverable worker fault during dispatch (for example a dropped database connection), raised once.</summary>
public sealed class TransientlyFailingChannel(INotificationChannel inner) : INotificationChannel
{
    public NotificationChannel ChannelType => inner.ChannelType;

    public string ProviderName => inner.ProviderName;

    public bool RequiresDurableFirstSend => inner.RequiresDurableFirstSend;

    public Task<NotificationDispatchResult> DispatchAsync(
        NotificationDispatchRequest request,
        SimulationDispatchScenario scenario,
        CancellationToken cancellationToken)
        => throw new InvalidOperationException("SIMULATION: transient worker fault.");
}

public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start.ToUniversalTime();
    private TimeSpan? jumpAfterNextRead;

    public override DateTimeOffset GetUtcNow()
    {
        var value = now;
        if (jumpAfterNextRead is { } jump)
        {
            jumpAfterNextRead = null;
            now = now.Add(jump);
        }

        return value;
    }

    public void Advance(TimeSpan duration) => now = now.Add(duration);

    /// <summary>Time passes right after the next read: models claim, lock waits and earlier recipients taking long.</summary>
    public void JumpAfterNextRead(TimeSpan duration) => jumpAfterNextRead = duration;
}

/// <summary>Fake ACS SMS endpoint: verifies the documented HMAC scheme independently and issues opaque message IDs.</summary>
public sealed class SigningFakeAcs(string accessKey, Func<TimeProvider> clock) : HttpMessageHandler
{
    private readonly byte[] key = Convert.FromBase64String(accessKey);
    private readonly ConcurrentQueue<HttpStatusCode> scripted = new();

    public ConcurrentQueue<FakeAcsRequest> RequestLog { get; } = new();

    public IReadOnlyList<FakeAcsRequest> Requests => RequestLog.ToArray();

    private readonly ConcurrentDictionary<string, (string FirstSent, string MessageId)> accepted = new();
    private CancellationTokenSource? crashAfterNextAccept;
    private int loseNextAcceptedResponse;

    public void Enqueue(HttpStatusCode status) => scripted.Enqueue(status);

    /// <summary>Distinct repeatable requests the fake accepted: each one is one SMS that would reach a handset.</summary>
    public int AcceptedSendCount => accepted.Count;

    /// <summary>Accept the next send, then lose its response (the caller sees an ambiguous 500).</summary>
    public void LoseNextAcceptedResponse() => Interlocked.Exchange(ref loseNextAcceptedResponse, 1);

    /// <summary>Accept the next send, then simulate the worker dying before its transaction commits.</summary>
    public void CrashAfterNextAccept(CancellationTokenSource workerShutdown) => crashAfterNextAccept = workerShutdown;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var date = request.Headers.GetValues("x-ms-date").Single();
        var hash = request.Headers.GetValues("x-ms-content-sha256").Single();
        var stringToSign = $"POST\n{request.RequestUri!.PathAndQuery}\n{date};{request.RequestUri.Authority};{hash}";
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(stringToSign)));
        using var json = JsonDocument.Parse(body);
        var recipient = json.RootElement.GetProperty("smsRecipients")[0];
        RequestLog.Enqueue(new FakeAcsRequest(
            request.Headers.Authorization?.ToString() == $"HMAC-SHA256 SignedHeaders=x-ms-date;host;x-ms-content-sha256&Signature={expected}"
                && hash == Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body))),
            recipient.GetProperty("repeatabilityRequestId").GetString()!,
            recipient.GetProperty("repeatabilityFirstSent").GetString()!));
        if (scripted.TryDequeue(out var status)) return new HttpResponseMessage(status);

        // ACS repeatable-request semantics: the same request ID replays the original result only when the
        // first-sent time also matches; a changed first-sent time is a different request and is rejected.
        var to = recipient.GetProperty("to").GetString();
        var repeatabilityId = recipient.GetProperty("repeatabilityRequestId").GetString()!;
        var firstSent = recipient.GetProperty("repeatabilityFirstSent").GetString()!;
        // Conservative model: ACS documents 5-minute repeatable-request tracking and 412 for Email/Rooms
        // (https://learn.microsoft.com/en-us/rest/api/communication/repeatable-requests); SMS retention is unverified.
        if (DateTimeOffset.Parse(firstSent, System.Globalization.CultureInfo.InvariantCulture) < clock().GetUtcNow().AddMinutes(-5))
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        var original = accepted.GetOrAdd(repeatabilityId, _ => (firstSent, $"acs-e2e-{accepted.Count + 1:D4}"));
        if (original.FirstSent != firstSent)
        {
            return new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    $"{{\"value\":[{{\"to\":\"{to}\",\"httpStatusCode\":400,\"repeatabilityResult\":\"rejected\",\"successful\":false}}]}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        }

        if (Interlocked.Exchange(ref loseNextAcceptedResponse, 0) == 1)
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);

        if (Interlocked.Exchange(ref crashAfterNextAccept, null) is { } shutdown)
        {
            await shutdown.CancelAsync();
            throw new OperationCanceledException(shutdown.Token);
        }

        return new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                $"{{\"value\":[{{\"to\":\"{to}\",\"messageId\":\"{original.MessageId}\",\"httpStatusCode\":202,\"repeatabilityResult\":\"accepted\",\"successful\":true}}]}}",
                Encoding.UTF8,
                "application/json"),
        };
    }
}

public sealed record FakeAcsRequest(bool SignatureValid, string RepeatabilityRequestId, string RepeatabilityFirstSent);
