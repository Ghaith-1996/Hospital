using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
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
/// Phase 12 slice 2 end-to-end flow (V1-V32): real API host, real PostgreSQL, the real outbox processor, the
/// provider-neutral voice channel against the test-only reference provider, and RS256-signed callbacks.
/// Writes TestResults/phase12/voice-e2e-evidence.json with safe identifiers and counts only.
/// </summary>
[Collection(VoiceEndToEndCollection.Name)]
public sealed class VoiceEndToEndTests(VoiceEndToEndFixture fixture)
{
    [Fact]
    public async Task PlaybackCompletedOnAnAnsweredCallIsDeliveredButNeverAcknowledged()
    {
        // V6, V7, V12, V13, V16, V18, V29
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);

        (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        attempt.Provider.Should().Be(ReferenceVoiceProvider.ProviderName);
        attempt.ProviderReference.Should().Be("call-e2e-0001");
        var create = fixture.Server.Requests.Should().ContainSingle().Subject;
        create.SignatureValid.Should().BeTrue();
        create.To.Should().Be(VoiceEndToEndFixture.TestNumber, "only the mapped test number is ever dialed");
        create.Text.Should().StartWith("SIMULATION:");
        create.Repeats.Should().Be(2);
        create.CallbackUri.Should().Be(VoiceEndToEndFixture.CallbackBase + VoiceEndToEndFixture.WebhookPath);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        create.Tag.Should().Be(tag);

        using (var answered = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-answered", attempt.ProviderReference, tag, "answered")))
            answered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted, "answering is not delivery");
        using (var mismatch = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-tag-mismatch", attempt.ProviderReference, "ca-" + new string('0', 32), "playback-completed")))
            mismatch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted, "a callback bound to another attempt changes nothing");
        using (var completed = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-played", attempt.ProviderReference, tag, "playback-completed")))
            completed.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var duplicate = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-played", attempt.ProviderReference, tag, "playback-completed")))
            duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var late = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-late-no-answer", attempt.ProviderReference, tag, "ended", "no-answer")))
            late.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var foreign = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-foreign", "call-not-ours-0001", "ca-" + new string('7', 32), "playback-completed")))
            foreign.StatusCode.Should().Be(HttpStatusCode.OK);

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        (await fixture.ProcessAsync()).Outcome.Should().Be("processed");

        await using var db = fixture.CreateContext();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        final.OpenedState.Should().Be(ObservationState.NotApplicable);
        var events = await db.DeliveryEvents.Where(row => row.DeliveryAttemptId == final.Id).OrderBy(row => row.ReceivedAtUtc).ToArrayAsync();
        events.Select(row => (row.EventType, row.ProviderEventId)).Should().BeEquivalentTo(new[]
        {
            ("submitted", $"voice:{ReferenceVoiceProvider.ProviderName}:{tag}:submitted"),
            ("call-answered", $"voice:{ReferenceVoiceProvider.ProviderName}:evt-answered"),
            ("delivered", $"voice:{ReferenceVoiceProvider.ProviderName}:evt-played"),
            ("failed", $"voice:{ReferenceVoiceProvider.ProviderName}:evt-late-no-answer"),
        });
        var inbox = await db.InboxMessages.Where(row => row.Handler == ProviderCallEventService.Handler(ReferenceVoiceProvider.ProviderName)).ToArrayAsync();
        inbox.Select(row => (row.ExternalMessageId, row.Result)).Should().BeEquivalentTo(new[]
        {
            ("evt-answered", "no-state-change"),
            ("evt-tag-mismatch", "rejected-tag-mismatch"),
            ("evt-played", "applied"),
            ("evt-late-no-answer", "no-state-change"),
        });
        (await db.PendingProviderCallEvents.CountAsync()).Should().Be(0, "an event matching nothing is dropped, not held");
        (await db.OutboxMessages.SingleAsync(row => row.AggregateId == alertId)).ProcessingState.Should().Be(OutboxProcessingState.Processed);

        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        var recipient = live.GetProperty("recipients").EnumerateArray().Single();
        var liveAttempt = recipient.GetProperty("attempts").EnumerateArray().Single();
        liveAttempt.GetProperty("status").GetString().Should().Be("Delivered");
        liveAttempt.GetProperty("openedState").GetString().Should().Be("NotApplicable");
        recipient.GetProperty("acknowledgedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        recipient.GetProperty("responsibilityAcceptedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);

        await fixture.AssertNoSensitiveValuesPersistedOrLoggedAsync();
        fixture.Record("answered-playback-delivered", new
        {
            attemptStatus = final.Status.ToString(),
            deliveryEvents = events.Select(row => row.EventType).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            inboxResults = inbox.Select(row => row.Result).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            liveAcknowledged = false,
            liveResponsibilityAccepted = false,
            createRequests = fixture.Server.Requests.Count,
            executedCalls = fixture.Server.ExecutedCalls,
            signaturesValid = fixture.Server.Requests.All(row => row.SignatureValid),
        });
    }

    [Theory]
    [InlineData("no-answer", "voice-no-answer")]
    [InlineData("busy", "voice-busy")]
    [InlineData("declined", "voice-declined")]
    [InlineData("unreachable", "voice-call-failed")]
    [InlineData("failed", "voice-call-failed")]
    [InlineData("answered+hung-up", "voice-playback-incomplete")]
    [InlineData("answered+completed", "voice-playback-incomplete")]
    [InlineData("playback-failed", "voice-playback-failed")]
    public async Task CallsThatDoNotFinishThePlaybackFailVisiblyWithTheirCategory(string script, string expectedCategory)
    {
        // V8, V9, V10, V31
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        var events = script switch
        {
            "answered+hung-up" => new[] { EventItem("evt-a", attempt.ProviderReference, tag, "answered"), EventItem("evt-b", attempt.ProviderReference, tag, "ended", "hung-up") },
            "answered+completed" => new[] { EventItem("evt-a", attempt.ProviderReference, tag, "answered"), EventItem("evt-b", attempt.ProviderReference, tag, "ended", "completed") },
            "playback-failed" => new[] { EventItem("evt-a", attempt.ProviderReference, tag, "answered"), EventItem("evt-b", attempt.ProviderReference, tag, "playback-failed") },
            _ => new[] { EventItem("evt-a", attempt.ProviderReference, tag, "ended", script) },
        };

        using (var response = await fixture.PostEventsAsync(fixture.ValidToken(), new { events }))
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var result = await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be(expectedCategory);
        fixture.Server.Requests.Should().ContainSingle("a failed call is never placed again by the channel");
        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        live.GetProperty("recipients")[0].GetProperty("attempts")[0].GetProperty("failureCategory").GetString().Should().Be(expectedCategory);
        live.GetProperty("operationalWarnings").EnumerateArray().Select(item => item.GetProperty("code").GetString()).Should().Contain("DeliveryFailed");
        fixture.Record($"call-ending-{script}", new { attemptStatus = "Failed", failureCategory = final.FailureCategory, liveCategory = expectedCategory });
    }

    [Fact]
    public async Task NoTerminalEventWithinTheOutcomeWindowFailsAsUnconfirmedWithoutASecondCall()
    {
        // V11, V13
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        for (var poll = 0; poll < 3; poll++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(6));
            (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(181));
        var result = await fixture.ProcessAsync();
        using (var late = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-late-played", attempt.ProviderReference, tag, "playback-completed")))
            late.StatusCode.Should().Be(HttpStatusCode.OK);

        result.PermanentlyFailed.Should().BeTrue();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be("call-outcome-unconfirmed");
        fixture.Server.Requests.Should().ContainSingle("waiting for the call outcome never places another call");
        await using var db = fixture.CreateContext();
        (await db.InboxMessages.SingleAsync(row => row.ExternalMessageId == "evt-late-played")).Result.Should().Be("no-state-change");
        (await db.AuditEvents.CountAsync(row => row.ResourceId == alertId && row.Action == "dispatch.retry-scheduled"))
            .Should().Be(1, "only the start of a call-outcome wait is audited");
        fixture.Record("call-outcome-unconfirmed", new { attemptStatus = "Failed", failureCategory = final.FailureCategory, latePlayback = "no-state-change", createRequests = 1 });
    }

    [Fact]
    public async Task CallbacksAreAuthenticatedBeforeAnythingIsRead()
    {
        // V14
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        var forged = Event("evt-forged", attempt.ProviderReference, tag, "playback-completed");
        var results = new Dictionary<string, int>();
        async Task Check(string name, string? token, HttpClient? client = null)
        {
            using var response = await fixture.PostEventsAsync(token, forged, client);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
            results[name] = (int)response.StatusCode;
        }

        await Check("no-token", null);
        await Check("bad-signature", fixture.Token(signingKey: VoiceEndToEndFixture.CreateKey("sim-callback-key")));
        await Check("unknown-key", fixture.Token(signingKey: VoiceEndToEndFixture.CreateKey("sim-attacker-key")));
        await Check("wrong-issuer", fixture.Token(issuer: "https://sim-someone-else.example.test"));
        await Check("wrong-audience", fixture.Token(audience: "sim-other-audience"));
        await Check("expired", fixture.Token(expires: DateTime.UtcNow.AddMinutes(-10)));
        await Check("overlong-lifetime", fixture.Token(issuedAt: DateTime.UtcNow.AddMinutes(-1), expires: DateTime.UtcNow.AddHours(2)));
        await Check("symmetric-algorithm", fixture.SymmetricToken());
        using (var cookieClient = await fixture.SignedInAsync(DemoDataSeeder.MorganHandle))
            await Check("development-cookie", null, cookieClient);

        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0);
        (await db.PendingProviderCallEvents.CountAsync()).Should().Be(0);
        (await db.AuditEvents.CountAsync(row => row.ActorType == "provider-webhook")).Should().Be(0);
        fixture.Record("callback-authentication", results);
    }

    [Fact]
    public async Task MalformedCallbackRequestsAreRejectedWhole()
    {
        // V15
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        var good = EventItem("evt-good", attempt.ProviderReference, tag, "playback-completed");
        var results = new Dictionary<string, int>();
        async Task Check(string name, HttpContent content, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, VoiceEndToEndFixture.WebhookPath) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ValidToken());
            using var response = await fixture.Client.SendAsync(request);
            response.StatusCode.Should().Be(expected, name);
            (await response.Content.ReadAsStringAsync()).Should().NotContain(VoiceEndToEndFixture.TestNumber);
            results[name] = (int)response.StatusCode;
        }

        HttpContent Batch(params object[] items) => Json(new { events = items });
        await Check("content-type", new StringContent(JsonSerializer.Serialize(new { events = new[] { good } }), Encoding.UTF8, "text/plain"), HttpStatusCode.UnsupportedMediaType);
        await Check("oversize", Json(new string(' ', 70 * 1024) + "{}"), HttpStatusCode.RequestEntityTooLarge);
        await Check("empty-batch", Batch(), HttpStatusCode.BadRequest);
        await Check("too-many-events", Batch(Enumerable.Range(0, 51).Select(index => (object)EventItem($"evt-{index}", attempt.ProviderReference, tag, "answered")).ToArray()), HttpStatusCode.BadRequest);
        await Check("unknown-kind", Batch(good, EventItem("evt-kind", attempt.ProviderReference, tag, "transferred")), HttpStatusCode.BadRequest);
        await Check("unknown-reason", Batch(good, EventItem("evt-reason", attempt.ProviderReference, tag, "ended", "voicemail")), HttpStatusCode.BadRequest);
        await Check("ended-without-reason", Batch(good, EventItem("evt-no-reason", attempt.ProviderReference, tag, "ended")), HttpStatusCode.BadRequest);
        await Check("reason-on-answered", Batch(good, EventItem("evt-extra-reason", attempt.ProviderReference, tag, "answered", "busy")), HttpStatusCode.BadRequest);
        await Check("unsafe-event-id", Batch(good, EventItem("evt with spaces", attempt.ProviderReference, tag, "answered")), HttpStatusCode.BadRequest);
        await Check("long-event-id", Batch(good, EventItem("evt-" + new string('x', 70), attempt.ProviderReference, tag, "answered")), HttpStatusCode.BadRequest);
        await Check("unsafe-call-id", Batch(good, EventItem("evt-call", "call id/../x", tag, "answered")), HttpStatusCode.BadRequest);
        await Check("unsafe-tag", Batch(good, EventItem("evt-tag", attempt.ProviderReference, "tag with spaces", "answered")), HttpStatusCode.BadRequest);
        await Check("stale", Batch(good, EventItem("evt-stale", attempt.ProviderReference, tag, "answered", time: DateTime.UtcNow.AddDays(-3))), HttpStatusCode.BadRequest);
        await Check("future", Batch(good, EventItem("evt-future", attempt.ProviderReference, tag, "answered", time: DateTime.UtcNow.AddHours(1))), HttpStatusCode.BadRequest);
        await Check("duplicate-id-in-batch", Batch(good, good), HttpStatusCode.BadRequest);
        await Check("not-json", new StringContent("{nope", Encoding.UTF8, "application/json"), HttpStatusCode.BadRequest);

        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0, "no event of a rejected request may be applied");
        (await db.PendingProviderCallEvents.CountAsync()).Should().Be(0);
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Record("callback-validation", results);
    }

    [Fact]
    public async Task CallbacksThatBeatTheSendCommitAreHeldAndAppliedInOrder()
    {
        // V17: the provider answers and finishes the call before the worker transaction that stores the call ID commits.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        var statuses = new List<HttpStatusCode>();
        fixture.Server.OnCallCreated = async (callId, tag) =>
        {
            var now = DateTime.UtcNow;
            using var response = await fixture.PostEventsAsync(fixture.ValidToken(), new
            {
                events = new[]
                {
                    EventItem("evt-race-played", callId, tag, "playback-completed", time: now.AddSeconds(-1)),
                    EventItem("evt-race-answered", callId, tag, "answered", time: now.AddSeconds(-2)),
                },
            });
            statuses.Add(response.StatusCode);
        };

        var result = await fixture.ProcessAsync();

        statuses.Should().Equal(HttpStatusCode.OK);
        result.Outcome.Should().Be("processed", "the held events are applied in the pass that records the call ID");
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        await using var db = fixture.CreateContext();
        var events = await db.DeliveryEvents.Where(row => row.DeliveryAttemptId == final.Id).ToArrayAsync();
        events.Select(row => row.EventType).Should().BeEquivalentTo(["submitted", "call-answered", "delivered"]);
        var pending = await db.PendingProviderCallEvents.OrderBy(row => row.OccurredAtUtc).ToArrayAsync();
        pending.Should().HaveCount(2).And.OnlyContain(row => row.AppliedAtUtc != null);
        pending.Select(row => row.ExternalEventId).Should().Equal("evt-race-answered", "evt-race-played");

        // Redelivery after the commit deduplicates against the applied events.
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, final.IdempotencyKey);
        using (var redelivered = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-race-played", final.ProviderReference, tag, "playback-completed")))
            redelivered.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.DeliveryEvents.CountAsync(row => row.DeliveryAttemptId == final.Id)).Should().Be(3);
        fixture.Record("callback-beats-send-commit", new
        {
            callbackStatus = statuses.Select(status => (int)status).ToArray(),
            heldEvents = pending.Length,
            appliedInOrder = pending.Select(row => row.Kind.ToString()).ToArray(),
            attemptStatus = final.Status.ToString(),
            outcome = result.Outcome,
        });
    }

    [Theory]
    [InlineData(FakeVoiceMode.AcceptThenLoseResponse)]
    [InlineData(FakeVoiceMode.AcceptWithMalformedBody)]
    [InlineData(FakeVoiceMode.AcceptWithStalledBody)]
    [InlineData(FakeVoiceMode.Hang)]
    [InlineData(FakeVoiceMode.Redirect)]
    public async Task IdempotentProviderRetriesAmbiguousOutcomesWithTheSameKeyAndPlacesOneCall(FakeVoiceMode mode)
    {
        // V19, V28
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(mode);

        var first = await fixture.ProcessAsync();
        var pending = await fixture.SingleAttemptAsync(alertId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var second = await fixture.ProcessAsync();
        var final = await fixture.SingleAttemptAsync(alertId);

        first.Outcome.Should().Be("rescheduled");
        pending.Status.Should().Be(DeliveryAttemptStatus.Requested, "an ambiguous outcome keeps the same attempt and key");
        pending.ProviderReference.Should().BeEmpty();
        second.Outcome.Should().Be("rescheduled");
        final.Id.Should().Be(pending.Id);
        final.Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Server.Requests.Should().HaveCount(2);
        fixture.Server.Requests.Select(row => row.RepeatabilityId).Distinct().Should().ContainSingle();
        fixture.Server.Requests.Select(row => row.FirstSent).Distinct().Should().ContainSingle();
        fixture.Server.ExecutedCalls.Should().BeLessThanOrEqualTo(1, "the idempotent provider executes one call per repeatability ID");
        fixture.Record($"ambiguous-same-key-{mode}", new
        {
            attempts = 1,
            createRequests = fixture.Server.Requests.Count,
            distinctRepeatabilityIds = 1,
            executedCalls = fixture.Server.ExecutedCalls,
            finalStatus = final.Status.ToString(),
        });
    }

    [Fact]
    public async Task AmbiguousOutcomeStopsRetryingAfterTheUncertainWindow()
    {
        // V19
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(FakeVoiceMode.AcceptThenLoseResponse);

        await fixture.ProcessAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        var result = await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be("provider-outcome-uncertain");
        fixture.Server.Requests.Should().ContainSingle("no replay after the uncertain window");
        fixture.Record("uncertain-window-closed", new { attemptStatus = "Failed", failureCategory = final.FailureCategory, createRequests = 1 });
    }

    [Fact]
    public async Task NonIdempotentProviderNeverRetriesAnAmbiguousOutcome()
    {
        // V20
        await fixture.ResetAsync();
        fixture.Server.Idempotent = false;
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(FakeVoiceMode.AcceptThenLoseResponse);

        var result = await fixture.ProcessAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be("provider-outcome-uncertain");
        fixture.Server.Requests.Should().ContainSingle("a second call at night is a duplicate dispatch");
        fixture.Server.ExecutedCalls.Should().Be(1);
        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        live.GetProperty("operationalWarnings").EnumerateArray().Select(item => item.GetProperty("code").GetString()).Should().Contain("DeliveryFailed");
        fixture.Record("non-idempotent-ambiguous", new { attemptStatus = "Failed", failureCategory = final.FailureCategory, createRequests = 1, executedCalls = 1 });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CrashAfterTheProviderCreatedTheCallNeverPlacesASecondCall(bool idempotent)
    {
        // V21
        await fixture.ResetAsync();
        fixture.Server.Idempotent = idempotent;
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        using var shutdown = new CancellationTokenSource();
        fixture.Server.CrashAfterNextAccept(shutdown);

        var crashed = () => fixture.ProcessAsync(shutdown.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();
        await using (var afterCrash = fixture.CreateContext())
            (await afterCrash.DeliveryAttempts.CountAsync(row => row.AlertId == new AlertId(alertId))).Should().Be(0, "the dispatch transaction rolled back");

        // Past the crashed worker's one-minute outbox lease.
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await fixture.ProcessAsync();
        var final = await fixture.SingleAttemptAsync(alertId);
        fixture.Server.ExecutedCalls.Should().Be(1);
        if (idempotent)
        {
            final.Status.Should().Be(DeliveryAttemptStatus.Submitted);
            final.ProviderReference.Should().Be("call-e2e-0001", "the replay recovers the original call");
            fixture.Server.Requests.Should().HaveCount(2);
            fixture.Server.Requests.Select(row => (row.RepeatabilityId, row.FirstSent)).Distinct().Should().ContainSingle();
        }
        else
        {
            final.Status.Should().Be(DeliveryAttemptStatus.Failed);
            final.FailureCategory.Should().Be("provider-outcome-uncertain");
            fixture.Server.Requests.Should().ContainSingle("a provider that cannot deduplicate is never asked again");
        }

        fixture.Record($"crash-after-create-{(idempotent ? "idempotent" : "non-idempotent")}", new
        {
            attemptStatus = final.Status.ToString(),
            createRequests = fixture.Server.Requests.Count,
            executedCalls = fixture.Server.ExecutedCalls,
        });
    }

    [Theory]
    [InlineData("test-number")]
    [InlineData("caller-id")]
    [InlineData("account")]
    public async Task ConfigurationDriftBetweenASendAndItsReplayIsNeverReplayed(string drift)
    {
        // V22
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(FakeVoiceMode.AcceptThenLoseResponse);
        await fixture.ProcessAsync();
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Requested);

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var overrides = drift switch
        {
            "test-number" => new Dictionary<string, string?> { ["Communications:Voice:TestRecipients:SIM-VOICE-0102"] = "+15555550178" },
            "caller-id" => new Dictionary<string, string?> { ["Communications:Voice:CallerId"] = "+15555550199" },
            _ => new Dictionary<string, string?>(),
        };
        await fixture.ProcessAsync(settings: overrides, accountIdentity: drift == "account" ? "sim-voice-account-0002" : ReferenceVoiceProvider.DefaultAccount);

        var final = await fixture.SingleAttemptAsync(alertId);
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be("provider-outcome-uncertain");
        fixture.Server.Requests.Should().ContainSingle("a replay under changed settings would be a different call");
        fixture.Record($"configuration-drift-{drift}", new { attemptStatus = "Failed", failureCategory = final.FailureCategory, createRequests = 1 });
    }

    [Fact]
    public async Task RestartWithTheSimulatedProviderAfterARealSendNeverClaimsSimulatedDelivery()
    {
        // V23
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        using var shutdown = new CancellationTokenSource();
        fixture.Server.CrashAfterNextAccept(shutdown);
        var crashed = () => fixture.ProcessAsync(shutdown.Token);
        await crashed.Should().ThrowAsync<OperationCanceledException>();

        // Past the crashed worker's one-minute outbox lease.
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        var result = await fixture.ProcessAsync(voiceConfigured: false);

        result.PermanentlyFailed.Should().BeTrue();
        var final = await fixture.SingleAttemptAsync(alertId);
        final.Provider.Should().Be(ReferenceVoiceProvider.ProviderName, "the attempt keeps the provider that placed the call");
        final.Status.Should().Be(DeliveryAttemptStatus.Failed);
        final.FailureCategory.Should().Be("delivery-unconfirmed");
        fixture.Server.Requests.Should().ContainSingle();
        fixture.Record("crash-then-simulation-restart", new { attemptProvider = final.Provider, attemptStatus = "Failed", failureCategory = final.FailureCategory });
    }

    [Fact]
    public async Task SubmittedCallSurvivesDirectoryChangesAndMappingRemoval()
    {
        // V24
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await fixture.ProcessAsync();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        await fixture.DeactivateRowanDirectoryEntryAsync();

        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        var unmapped = new Dictionary<string, string?>
        {
            ["Communications:Voice:TestRecipients:SIM-VOICE-0102"] = null,
            ["Communications:Voice:TestRecipients:SIM-VOICE-9999"] = "+15555550179",
        };
        (await fixture.ProcessAsync(settings: unmapped)).Outcome.Should().Be("rescheduled");
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, attempt.IdempotencyKey);
        using (var played = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-after-directory-change", attempt.ProviderReference, tag, "playback-completed")))
            played.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        (await fixture.ProcessAsync(settings: unmapped)).Outcome.Should().Be("processed");

        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Delivered);
        fixture.Server.Requests.Should().ContainSingle();
        fixture.Record("submitted-survives-directory-change", new { attemptStatus = "Delivered", createRequests = 1 });
    }

    [Theory]
    [InlineData(FakeVoiceMode.Unauthorized, "provider-auth-failed")]
    [InlineData(FakeVoiceMode.BadRequest, "voice-call-rejected")]
    public async Task DefiniteProviderRefusalsFailVisiblyWithoutRetry(FakeVoiceMode mode, string category)
    {
        // V25, V26
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(mode);

        var result = await fixture.ProcessAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be(category);
        fixture.Server.Requests.Should().ContainSingle();
        fixture.Record($"provider-refusal-{category}", new { attemptStatus = "Failed", failureCategory = category, createRequests = 1 });
    }

    [Fact]
    public async Task DocumentedNonExecutionRetriesAsANewBoundedAttempt()
    {
        // V27
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        fixture.Server.Enqueue(FakeVoiceMode.Throttled);

        (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        await fixture.ProcessAsync();

        await using var db = fixture.CreateContext();
        var attempts = await db.DeliveryAttempts.AsNoTracking().Where(row => row.AlertId == new AlertId(alertId)).OrderBy(row => row.AttemptNumber).ToArrayAsync();
        attempts.Should().HaveCount(2);
        attempts[0].Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempts[0].FailureCategory.Should().Be("provider-unavailable");
        attempts[1].Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Server.Requests.Select(row => row.RepeatabilityId).Distinct().Should().HaveCount(2, "a new attempt has a new key");
        fixture.Server.ExecutedCalls.Should().Be(1);
        fixture.Record("documented-non-execution", new { attempts = attempts.Length, firstCategory = attempts[0].FailureCategory, secondStatus = attempts[1].Status.ToString(), executedCalls = 1 });
    }

    [Fact]
    public async Task EndpointWithoutATestMappingFailsWithoutCallingTheProvider()
    {
        // V4
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        var unmapped = new Dictionary<string, string?>
        {
            ["Communications:Voice:TestRecipients:SIM-VOICE-0102"] = null,
            ["Communications:Voice:TestRecipients:SIM-VOICE-9999"] = "+15555550179",
        };

        var result = await fixture.ProcessAsync(settings: unmapped);

        result.PermanentlyFailed.Should().BeTrue();
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("test-recipient-not-configured");
        fixture.Server.Requests.Should().BeEmpty();
        fixture.Record("test-recipient-not-configured", new { attemptStatus = "Failed", failureCategory = attempt.FailureCategory, createRequests = 0 });
    }

    [Theory]
    [InlineData("missing-prefix", "please open the secure alert application.")]
    [InlineData("control-character", "SIMULATION: please open\u0007 the secure alert application.")]
    public async Task UnsafeSpokenTextIsNeverSentToTheProvider(string name, string template)
    {
        // V5: the approved policy template is the only spoken text; unsafe content fails before any provider call.
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        await using (var db = fixture.CreateContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE notification_policies SET generic_voice_template = {template}");

        var result = await fixture.ProcessAsync();

        result.PermanentlyFailed.Should().BeTrue();
        await using var check = fixture.CreateContext();
        (await check.OutboxMessages.SingleAsync(row => row.AggregateId == alertId)).LastErrorCategory.Should().Be("dispatch-validation");
        fixture.Server.Requests.Should().BeEmpty();
        fixture.Record($"unsafe-spoken-text-{name}", new { outcome = result.Outcome, createRequests = 0 });
    }

    [Fact]
    public async Task LatePrimaryVoiceFailureKeepsAnApprovedBackupWorkflowUsable()
    {
        // V30
        await fixture.ResetAsync(escalationStepDelay: TimeSpan.FromSeconds(1));
        await fixture.SeedRileyCurrentBackupOnCallAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        (await fixture.ProcessAsync()).Outcome.Should().Be("rescheduled");
        var primary = await fixture.SingleVoiceAttemptAsync(alertId);
        await fixture.RunEscalationUntilBackupQueuedAsync(alertId);
        fixture.SyncClockToRealTime();
        for (var pass = 0; pass < 4 && !await fixture.BackupDeliveredAsync(alertId); pass++)
            await fixture.ProcessAsync();
        (await fixture.BackupDeliveredAsync(alertId)).Should().BeTrue("the approved backup secure message is delivered");

        var tag = ProviderVoiceChannel.CreateTag(ReferenceVoiceProvider.ProviderName, primary.IdempotencyKey);
        using (var failed = await fixture.PostEventsAsync(fixture.ValidToken(), Event("evt-primary-busy", primary.ProviderReference, tag, "ended", "busy")))
            failed.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        for (var pass = 0; pass < 3; pass++) await fixture.ProcessAsync();

        await using var db = fixture.CreateContext();
        var finalPrimary = await fixture.SingleVoiceAttemptAsync(alertId);
        var alert = await db.Alerts.AsNoTracking().SingleAsync(row => row.Id == new AlertId(alertId));
        finalPrimary.Status.Should().Be(DeliveryAttemptStatus.Failed);
        finalPrimary.FailureCategory.Should().Be("voice-busy");
        alert.State.Should().Be(AlertState.Active, "a delivered backup keeps the alert workable");
        var live = await operatorClient.GetFromJsonAsync<JsonElement>($"/api/v1/alerts/{alertId:D}/live");
        live.GetProperty("operationalWarnings").EnumerateArray().Select(item => item.GetProperty("code").GetString()).Should().Contain("DeliveryFailed");
        fixture.Record("backup-delivered-then-primary-voice-busy", new { primaryAttempt = "Failed", primaryCategory = "voice-busy", alertState = alert.State.ToString() });
    }

    [Fact]
    public async Task UnknownProviderPathIsNotFound()
    {
        // V32
        await fixture.ResetAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/communications/voice/other-provider")
        {
            Content = Json(new { events = new[] { EventItem("evt-other", "call-e2e-0001", "ca-" + new string('1', 32), "answered") } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ValidToken());
        using var response = await fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0);
        fixture.Record("unknown-provider-path", new { status = (int)response.StatusCode });
    }

    [Fact]
    public async Task VoiceDefaultsToSimulationAndFailsClosedWhenMisconfigured()
    {
        // V1, V2, V3
        await fixture.ResetAsync();
        using var operatorClient = await fixture.SignedInAsync(DemoDataSeeder.JordanHandle);
        var alertId = await fixture.CreateConfirmedVoiceAlertAsync(operatorClient);
        var simulated = await fixture.ProcessAsync(voiceConfigured: false);
        simulated.Outcome.Should().Be("processed");
        var attempt = await fixture.SingleAttemptAsync(alertId);
        attempt.Provider.Should().Be("simulation-voice");
        attempt.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        fixture.Server.Requests.Should().BeEmpty();

        static IReadOnlyList<INotificationChannel> Channels(IDictionary<string, string?> settings, string environment, bool registerProvider)
        {
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddLogging();
            services.AddDbContext<CriticalAlertsDbContext>(options => options.UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused;Password=unused"));
            if (registerProvider)
                services.AddSingleton<IVoiceCallProvider>(new ReferenceVoiceProvider(new FakeVoiceServer("sim-key"), "sim-key"));
            services.AddSimulationDispatch();
            services.AddConfiguredVoiceProvider(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), environment);
            using var provider = services.BuildServiceProvider();
            DispatchServiceCollectionExtensions.EnsureConfiguredVoiceProviderRegistered(provider);
            using var scope = provider.CreateScope();
            return scope.ServiceProvider.GetServices<INotificationChannel>().ToArray();
        }

        Channels(new Dictionary<string, string?>(), "Test", registerProvider: false)
            .Single(channel => channel.ChannelType == NotificationChannel.Voice).Should().BeOfType<SimulationVoiceChannel>();
        Channels(VoiceEndToEndFixture.VoiceSettings(), "Test", registerProvider: true)
            .Single(channel => channel.ChannelType == NotificationChannel.Voice).Should().BeOfType<ProviderVoiceChannel>();
        FluentActions.Invoking(() => Channels(VoiceEndToEndFixture.VoiceSettings(), "Test", registerProvider: false))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("not registered");
        FluentActions.Invoking(() => Channels(VoiceEndToEndFixture.VoiceSettings(), "Production", registerProvider: true))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("REQUIRES_HOSPITAL_DECISION");

        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Communications:Voice:Provider", ReferenceVoiceProvider.ProviderName);
            builder.UseSetting("Communications:Webhooks:Voice:Enabled", "true");
        });
        FluentActions.Invoking(() => production.CreateClient()).Should().Throw<InvalidOperationException>();

        using var unregistered = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Communications:Voice:Provider", "sim-unregistered");
            builder.UseSetting("Communications:Webhooks:Voice:Enabled", "true");
        });
        FluentActions.Invoking(() => unregistered.CreateClient()).Should().Throw<InvalidOperationException>();

        using var disabled = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        });
        using var client = disabled.CreateClient();
        using var response = await client.PostAsync(VoiceEndToEndFixture.WebhookPath, new StringContent("{}", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.Record("provider-selection", new
        {
            defaultChannel = "SimulationVoiceChannel",
            defaultAttemptStatus = attempt.Status.ToString(),
            configuredChannel = "ProviderVoiceChannel",
            unregisteredRefused = true,
            productionRefused = true,
            webhookAbsentByDefault = true,
        });
    }

    private static object Event(string id, string callId, string tag, string type, string? reason = null, DateTime? time = null)
        => new { events = new[] { EventItem(id, callId, tag, type, reason, time) } };

    private static Dictionary<string, object> EventItem(string id, string callId, string tag, string type, string? reason = null, DateTime? time = null)
    {
        var item = new Dictionary<string, object>
        {
            ["id"] = id,
            ["callId"] = callId,
            ["context"] = tag,
            ["type"] = type,
            ["time"] = (time ?? DateTime.UtcNow).ToString("O"),
            // Provider fields the pipeline must never read, store or log.
            ["to"] = VoiceEndToEndFixture.TestNumber,
            ["from"] = VoiceEndToEndFixture.CallerId,
            ["detail"] = "SIMULATION provider detail " + VoiceEndToEndFixture.TestNumber,
        };
        if (reason is not null) item["reason"] = reason;
        return item;
    }

    private static StringContent Json(object value)
        => new(value as string ?? JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}

public sealed class VoiceEndToEndFixture : IAsyncLifetime
{
    public const string CallerId = "+15555550100";
    public const string TestNumber = "+15555550177";
    public const string CallbackBase = "https://sim-critical-alerts.example.test";
    public const string WebhookPath = "/api/v1/webhooks/communications/voice/" + ReferenceVoiceProvider.ProviderName;
    public const string CallbackIssuer = "https://sim-voice-provider.example.test";
    public const string CallbackAudience = "sim-critical-alerts-voice";
    public static readonly string SharedKey = Convert.ToBase64String(Enumerable.Range(60, 32).Select(value => (byte)value).ToArray());
    private static readonly RsaSecurityKey SigningKey = CreateKey("sim-callback-key");

    private readonly PostgresApiFixture inner = new();
    private readonly CapturingLoggerProvider logs = new();
    private readonly ConcurrentDictionary<string, object> evidence = new();
    private readonly List<string> issuedTokens = [];
    private string dataProtectionKey = string.Empty;
    private WebApplicationFactory<Program>? factory;

    public MutableClock Clock { get; private set; } = new(DateTimeOffset.UtcNow);

    public FakeVoiceServer Server { get; private set; } = new(SharedKey);

    public HttpClient Client { get; private set; } = null!;

    public static Dictionary<string, string?> VoiceSettings() => new()
    {
        ["Communications:Voice:Provider"] = ReferenceVoiceProvider.ProviderName,
        ["Communications:Voice:CallerId"] = CallerId,
        ["Communications:Voice:CallbackBaseUri"] = CallbackBase,
        ["Communications:Voice:TestRecipients:SIM-VOICE-0102"] = TestNumber,
    };

    /// <summary>A fresh key with a given ID: same ID as the trusted key but different material models a forged signature.</summary>
    public static RsaSecurityKey CreateKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    public async Task InitializeAsync()
    {
        await inner.InitializeAsync();
        dataProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", inner.ConnectionString);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
            builder.UseSetting("SimulationResponses:Enabled", "true");
            builder.UseSetting("DataProtection:Key", dataProtectionKey);
            foreach (var (key, value) in VoiceSettings()) builder.UseSetting(key, value);
            builder.UseSetting("Communications:Webhooks:Voice:Enabled", "true");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.Configure<RateLimiterOptions>(options =>
                {
                    options.AddPolicy("api", _ => RateLimitPartition.GetNoLimiter("phase12-voice-e2e"));
                    options.AddPolicy("webhook", _ => RateLimitPartition.GetNoLimiter("phase12-voice-e2e-webhook"));
                });
                // The test provider's published key set; validation itself is the production JwtCallbackAuthenticator.
                var keys = new OpenIdConnectConfiguration { Issuer = CallbackIssuer };
                keys.SigningKeys.Add(SigningKey);
                services.AddSingleton<IVoiceCallbackReader>(new ReferenceVoiceCallbackReader(new JwtCallbackAuthenticator(
                    CallbackIssuer, CallbackAudience, new StaticConfigurationManager<OpenIdConnectConfiguration>(keys))));
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
            suite = "phase12-voice-e2e",
            simulationOnly = true,
            liveProviderCalled = false,
            provider = "test-only reference fake",
            scenarios = evidence.OrderBy(item => item.Key, StringComparer.Ordinal).ToDictionary(item => item.Key, item => item.Value),
        }, new JsonSerializerOptions { WriteIndented = true });
        foreach (var forbidden in ForbiddenValues())
            if (artifact.Contains(forbidden, StringComparison.Ordinal))
                throw new InvalidOperationException("The Phase 12 voice evidence artifact contains a sensitive value.");
        await File.WriteAllTextAsync(Path.Combine(directory, "voice-e2e-evidence.json"), artifact);
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
        Server = new FakeVoiceServer(SharedKey);
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
        DateTime? expires = null,
        DateTime? issuedAt = null,
        RsaSecurityKey? signingKey = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(4);
        var issued = issuedAt ?? expiry.AddMinutes(-5);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? CallbackIssuer,
            Audience = audience ?? CallbackAudience,
            IssuedAt = issued,
            NotBefore = issued,
            Expires = expiry,
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
        lock (issuedTokens) issuedTokens.Add(token);
        return token;
    }

    /// <summary>A correctly formed token signed with a shared secret instead of the provider's RSA key.</summary>
    public string SymmetricToken()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = CallbackIssuer,
            Audience = CallbackAudience,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(4),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(SharedKey)) { KeyId = "sim-callback-key" },
                SecurityAlgorithms.HmacSha256),
        });
        lock (issuedTokens) issuedTokens.Add(token);
        return token;
    }

    public async Task<HttpResponseMessage> PostEventsAsync(string? token, object body, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await (client ?? Client).SendAsync(request);
    }

    public async Task<Guid> CreateConfirmedVoiceAlertAsync(HttpClient client)
    {
        using var create = await client.PostAsJsonAsync("/api/v1/alerts/drafts", new CreateAlertDraftRequest(
            DemoDataSeeder.NorthSiteId.Value,
            DemoDataSeeder.EmergencyDepartmentId.Value,
            "SIM-PAT-PHASE12-0002",
            "North Wing / Simulation Room 205",
            "Urgent",
            "SIMULATION: phase twelve voice source",
            new AlertSbarDraft("SIMULATION: s", "SIMULATION: b", "SIMULATION: a", "SIMULATION: r"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]));
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var draft = (await create.Content.ReadFromJsonAsync<AlertDraftView>())!;
        using var approved = await client.PutAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, "SIMULATION: phase twelve approved voice message"));
        approved.EnsureSuccessStatusCode();
        var approvedDraft = (await approved.Content.ReadFromJsonAsync<AlertDraftView>())!;
        var rowan = (await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/v1/directory/practitioners?q=Rowan&includeInactive=false"))!.Single();
        using var recipients = await client.PutAsJsonAsync($"/api/v1/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(approvedDraft.DraftVersion,
                [new AlertRecipientInput(rowan.PractitionerId, rowan.PractitionerRoleId, "Voice", rowan.SelectionRevision)]));
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

    /// <summary>
    /// One worker pass. <paramref name="voiceConfigured"/> false models a worker restarted with the simulated voice provider;
    /// <paramref name="settings"/> overrides the voice settings for this pass (null removes a key).
    /// </summary>
    public async Task<DispatchProcessingResult> ProcessAsync(
        CancellationToken cancellationToken = default,
        bool voiceConfigured = true,
        IDictionary<string, string?>? settings = null,
        string accountIdentity = ReferenceVoiceProvider.DefaultAccount)
    {
        await using var db = CreateContext();
        INotificationChannel voice;
        if (voiceConfigured)
        {
            var values = VoiceSettings();
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                if (value is null) values.Remove(key);
                else values[key] = value;
            }

            var options = VoiceDispatchOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), "Test")!;
            voice = new ProviderVoiceChannel(options, new ReferenceVoiceProvider(Server, SharedKey, accountIdentity), db, Clock,
                TimeSpan.FromMilliseconds(500));
        }
        else
        {
            voice = new SimulationVoiceChannel(Clock);
        }

        var processor = new OutboxDispatchProcessor(
            db,
            [new SimulationSecureMessageChannel(Clock), new SimulationSmsChannel(Clock), voice],
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
        return await processor.ProcessNextAsync("phase12-voice-e2e-worker", cancellationToken);
    }

    public async Task DeactivateRowanDirectoryEntryAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE contact_endpoints SET is_active = false WHERE practitioner_id = {DemoDataSeeder.RowanPatelId.Value}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE practitioners SET is_active = false WHERE id = {DemoDataSeeder.RowanPatelId.Value}");
    }

    public async Task SeedRileyCurrentBackupOnCallAsync()
    {
        var now = DateTimeOffset.UtcNow;
        now = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        await using var db = CreateContext();
        db.PractitionerRoles.Add(Domain.Directory.PractitionerRoleAssignment.Create(PractitionerRoleId.New(),
            DemoDataSeeder.OrganizationId, DemoDataSeeder.RileySatoId, DemoDataSeeder.EmergencyDepartmentId,
            "Fictional emergency cover", false, "SIM-DIRECTORY", "SIM-ROLE-RILEY-PHASE12-VOICE"));
        db.OnCallAssignments.Add(Domain.Directory.OnCallAssignment.Create(OnCallAssignmentId.New(), DemoDataSeeder.OrganizationId,
            DemoDataSeeder.RileySatoId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            OnCallTier.Backup, now.AddHours(-2), now.AddHours(2), "SIM-ROSTER", "SIM-PHASE12-VOICE-BACKUP", now.AddMinutes(-5)));
        await db.SaveChangesAsync();
    }

    public async Task RunEscalationUntilBackupQueuedAsync(Guid alertId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            await using (var db = CreateContext())
            {
                await new EscalationProcessor(db).ProcessNextAsync("phase12-voice-e2e-escalation");
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

    public void SyncClockToRealTime() => Clock = new MutableClock(DateTimeOffset.UtcNow.AddSeconds(1));

    public async Task<DeliveryAttempt> SingleAttemptAsync(Guid alertId)
    {
        await using var db = CreateContext();
        return await db.DeliveryAttempts.AsNoTracking().SingleAsync(row => row.AlertId == new AlertId(alertId));
    }

    public async Task<DeliveryAttempt> SingleVoiceAttemptAsync(Guid alertId)
    {
        await using var db = CreateContext();
        return await db.DeliveryAttempts.AsNoTracking().SingleAsync(row => row.AlertId == new AlertId(alertId) && row.Channel == NotificationChannel.Voice);
    }

    public async Task AssertNoSensitiveValuesPersistedOrLoggedAsync()
    {
        await using var db = CreateContext();
        var persisted = new List<string>();
        persisted.AddRange(await db.DeliveryAttempts.Select(row => row.ProviderReference + "|" + row.FailureCategory + "|" + row.IdempotencyKey).ToArrayAsync());
        persisted.AddRange(await db.DeliveryEvents.Select(row => row.ProviderEventId + "|" + row.SanitizedMetadata + "|" + row.EventType).ToArrayAsync());
        persisted.AddRange(await db.InboxMessages.Select(row => row.ExternalMessageId + "|" + row.Result).ToArrayAsync());
        persisted.AddRange(await db.PendingProviderCallEvents.Select(row => row.ExternalEventId + "|" + row.CallId + "|" + row.CallbackTag).ToArrayAsync());
        persisted.AddRange(await db.ProviderSendRecords.Select(row => row.AttemptIdempotencyKey + "|" + row.CallbackTag + "|" + row.OperationFingerprint).ToArrayAsync());
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
        yield return CallerId;
        yield return CallerId[1..];
        yield return SharedKey;
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
public sealed class VoiceEndToEndCollection : ICollectionFixture<VoiceEndToEndFixture>
{
    public const string Name = "phase12-voice-e2e";
}
