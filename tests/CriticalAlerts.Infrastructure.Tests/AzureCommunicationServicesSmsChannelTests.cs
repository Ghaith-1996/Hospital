using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

/// <summary>
/// Isolated provider contract checks for failure modes F2-F17 in
/// docs/superpowers/specs/2026-09-29-phase-12-acs-sms-adapter-design.md.
/// The fake transport verifies the HMAC signature independently from the adapter.
/// </summary>
public sealed class AzureCommunicationServicesSmsChannelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-29T12:00:00Z");
    private static readonly string AccessKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    private const string Endpoint = "https://sim-critical-alerts.communication.azure.com";
    private const string FromNumber = "+18005550100";
    private const string TestNumber = "+15555550142";
    private const string Label = "SIM-SMS-0101";
    private const string DefaultKey = "use-default-test-key";

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("Unknown")]
    public void AcsIsRefusedOutsideDevelopmentTestAndStaging(string environment)
    {
        var create = () => Options(environment: environment);

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain(AccessKey);
    }

    [Theory]
    [InlineData("http://sim-critical-alerts.communication.azure.com")]
    [InlineData("https://sim-critical-alerts.communication.azure.com/sms")]
    [InlineData("https://sim-critical-alerts.communication.azure.com?x=1")]
    [InlineData("https://sim-critical-alerts.communication.azure.com:8443")]
    [InlineData("https://user@sim-critical-alerts.communication.azure.com")]
    [InlineData("https://example.com")]
    [InlineData("https://communication.azure.com")]
    [InlineData("not a uri")]
    public void EndpointMustBeAnAcsHttpsResourceHost(string endpoint)
    {
        var create = () => Options(endpoint: endpoint);

        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!")]
    [InlineData("AAAA")]
    public void AccessKeyMustBeBase64OfAtLeast32Bytes(string? key)
    {
        var create = () => Options(accessKey: key);

        create.Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("not-base64");
    }

    [Fact]
    public void TestRecipientsMustBeNonEmptySyntheticLabelsMappedToE164Numbers()
    {
        FluentActions.Invoking(() => Options(recipients: new Dictionary<string, string>()))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Options(recipients: new Dictionary<string, string> { [Label] = "555-0142" }))
            .Should().Throw<InvalidOperationException>().Which.Message.Should().NotContain("555-0142");
        FluentActions.Invoking(() => Options(recipients: new Dictionary<string, string> { ["dr.maya"] = TestNumber }))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => Options(fromNumber: "5550100"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task UnmappedEndpointLabelFailsWithoutAnyHttpCall()
    {
        var transport = new FakeAcsTransport();
        var channel = Channel(transport);

        var result = await channel.DispatchAsync(Request(endpointReference: "SIM-SMS-0999"), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        transport.Requests.Should().BeEmpty();
        result.Retryable.Should().BeFalse();
        result.Events.Should().ContainSingle(item => item.EventType == "failed" && item.FailureCategory == "test-recipient-not-configured");
    }

    [Theory]
    [InlineData("Your patient in room 204 needs you")]
    [InlineData("SIMULATION: \u0007 bell")]
    [InlineData("SIMULATION: é accented")]
    public async Task WakeUpTextMustBeGenericSimulationAsciiWithinOneSegment(string text)
    {
        var transport = new FakeAcsTransport();
        var channel = Channel(transport);

        var dispatch = () => channel.DispatchAsync(Request(wakeUpText: text), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        await dispatch.Should().ThrowAsync<DispatchValidationException>();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OverlongWakeUpTextIsRejected()
    {
        var transport = new FakeAcsTransport();

        var dispatch = () => Channel(transport).DispatchAsync(
            Request(wakeUpText: "SIMULATION: " + new string('x', 150)), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        await dispatch.Should().ThrowAsync<DispatchValidationException>();
        transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptedSendIsSignedMinimalAndRecordedOnlyAsSubmitted()
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(Accepted("acs-message-0001"));

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.DelayedDelivery, CancellationToken.None);

        var sent = transport.Requests.Should().ContainSingle().Subject;
        sent.SignatureValid.Should().BeTrue();
        sent.PathAndQuery.Should().Be("/sms?api-version=2021-03-07");
        using var body = JsonDocument.Parse(sent.Body);
        body.RootElement.EnumerateObject().Select(item => item.Name).Should().BeEquivalentTo("from", "smsRecipients", "message", "smsSendOptions");
        body.RootElement.GetProperty("from").GetString().Should().Be(FromNumber);
        body.RootElement.GetProperty("message").GetString().Should().Be("SIMULATION: urgent secure message available.");
        var recipient = body.RootElement.GetProperty("smsRecipients").EnumerateArray().Should().ContainSingle().Subject;
        recipient.GetProperty("to").GetString().Should().Be(TestNumber);
        recipient.GetProperty("repeatabilityFirstSent").GetString().Should().Be(Now.ToString("r"));
        body.RootElement.GetProperty("smsSendOptions").GetProperty("enableDeliveryReport").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("smsSendOptions").GetProperty("tag").GetString()
            .Should().Be(AzureCommunicationServicesSmsChannel.CreateTag(Request().IdempotencyKey));
        sent.Body.Should().NotContain("alert:").And.NotContain("dispatch:").And.NotContain(Label);

        result.ProviderReference.Should().Be("acs-message-0001");
        result.Retryable.Should().BeFalse();
        result.Events.Should().ContainSingle().Which.EventType.Should().Be("submitted");
        result.Events.Should().NotContain(item => item.EventType == "delivered");
        result.Events.Single().SanitizedMetadata.Should().NotContain(TestNumber);
    }

    [Fact]
    public async Task ReinvokingTheSameAttemptSendsIdenticalRepeatabilityValues()
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(_ => throw new HttpRequestException("connection reset"));
        transport.Enqueue(Accepted("acs-message-0002", repeatability: "accepted"));
        var channel = Channel(transport);

        var first = await channel.DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);
        var second = await channel.DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        first.Retryable.Should().BeTrue();
        first.Events.Should().BeEmpty();
        first.ProviderReference.Should().BeEmpty();
        second.Events.Should().ContainSingle(item => item.EventType == "submitted");
        transport.Requests.Should().HaveCount(2);
        transport.Requests.Select(item => item.RepeatabilityRequestId).Distinct().Should().ContainSingle()
            .Which.Should().MatchRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
        transport.Requests.Select(item => item.RepeatabilityFirstSent).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData(400, "sms-rejected", false)]
    [InlineData(429, "provider-unavailable", true)]
    [InlineData(503, "provider-unavailable", true)]
    public async Task ItemLevelFailuresWithoutMessageIdAreDefinite(int itemStatus, string category, bool retryable)
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(Item(itemStatus, successful: false, messageId: null));

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.Retryable.Should().Be(retryable);
        result.FailureCategory.Should().Be(category);
        result.Events.Should().ContainSingle(item => item.EventType == "failed" && item.FailureCategory == category);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "provider-auth-failed", false)]
    [InlineData(HttpStatusCode.Forbidden, "provider-auth-failed", false)]
    [InlineData(HttpStatusCode.BadRequest, "sms-rejected", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "provider-unavailable", true)]
    public async Task OverallDefiniteFailuresAreMappedToSafeCategories(HttpStatusCode status, string category, bool retryable)
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent($"{{\"error\":{{\"code\":\"x\",\"message\":\"{TestNumber} secret detail\"}}}}", Encoding.UTF8, "application/json"),
        });

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.FailureCategory.Should().Be(category);
        result.Retryable.Should().Be(retryable);
        result.Events.Should().ContainSingle(item => item.EventType == "failed");
        result.Events.Single().SanitizedMetadata.Should().NotContain(TestNumber).And.NotContain("secret");
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("request-timeout")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    [InlineData("redirect")]
    [InlineData("unsafe-message-id")]
    [InlineData("two-items")]
    public async Task AmbiguousOutcomesKeepTheAttemptRequestedForSameKeyRetry(string outcome)
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(outcome switch
        {
            "server-error" => _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            "request-timeout" => _ => new HttpResponseMessage(HttpStatusCode.RequestTimeout),
            "timeout" => _ => throw new TaskCanceledException("timeout", new TimeoutException()),
            "malformed" => _ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{not json", Encoding.UTF8, "application/json") },
            "redirect" => _ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect) { Headers = { Location = new Uri("https://attacker.example/sms") } },
            "unsafe-message-id" => Accepted("id with spaces and " + TestNumber),
            "two-items" => _ => Json(HttpStatusCode.Accepted, "{\"value\":[{\"to\":\"+15555550142\",\"messageId\":\"a\",\"httpStatusCode\":202,\"successful\":true},{\"to\":\"+15555550143\",\"messageId\":\"b\",\"httpStatusCode\":202,\"successful\":true}]}"),
            _ => throw new InvalidOperationException(),
        });

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.Retryable.Should().BeTrue();
        result.Events.Should().BeEmpty();
        result.ProviderReference.Should().BeEmpty();
        result.FailureCategory.Should().Be("provider-outcome-uncertain");
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("\"accepted\"")]
    [InlineData("{\"value\":\"x\"}")]
    [InlineData("{\"value\":[null]}")]
    [InlineData("{\"value\":[42]}")]
    [InlineData("{\"value\":[{\"httpStatusCode\":\"202\",\"successful\":false}]}")]
    [InlineData("{\"value\":[{\"httpStatusCode\":202.5,\"successful\":false}]}")]
    [InlineData("{\"value\":[{\"httpStatusCode\":202,\"successful\":\"true\",\"messageId\":\"acs-1\"}]}")]
    [InlineData("{\"value\":[{\"httpStatusCode\":202,\"successful\":true,\"messageId\":42}]}")]
    [InlineData("{\"value\":[{\"httpStatusCode\":400,\"successful\":false,\"repeatabilityResult\":7}]}")]
    public async Task ValidJsonOfTheWrongShapeIsAnUncertainOutcome(string body)
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(_ => Json(HttpStatusCode.Accepted, body));

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.Retryable.Should().BeTrue();
        result.Events.Should().BeEmpty();
        result.ProviderReference.Should().BeEmpty();
        result.FailureCategory.Should().Be("provider-outcome-uncertain");
    }

    [Fact]
    public async Task UncertainWindowExpiryFailsVisiblyWithoutAnotherSend()
    {
        var transport = new FakeAcsTransport();
        var clock = new FixedTime(Now.AddMinutes(10));

        var result = await Channel(transport, clock).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        transport.Requests.Should().BeEmpty();
        result.Retryable.Should().BeFalse();
        result.Events.Should().ContainSingle(item => item.EventType == "failed" && item.FailureCategory == "provider-outcome-uncertain");
    }

    [Fact]
    public async Task RepeatabilityRejectionIsADefiniteVisibleFailure()
    {
        var transport = new FakeAcsTransport();
        transport.Enqueue(_ => Json(HttpStatusCode.Accepted,
            "{\"value\":[{\"to\":\"+15555550142\",\"httpStatusCode\":400,\"repeatabilityResult\":\"rejected\",\"successful\":false}]}"));

        var result = await Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.Retryable.Should().BeFalse();
        result.Events.Should().ContainSingle(item => item.FailureCategory == "provider-repeatability-rejected");
    }

    [Fact]
    public async Task SubmittedAttemptsWaitForTheWebhookWithoutResendingThenFailAsUnconfirmed()
    {
        var transport = new FakeAcsTransport();
        var waiting = Request(status: DeliveryAttemptStatus.Submitted, submittedAt: Now.AddSeconds(-30));
        var expired = Request(status: DeliveryAttemptStatus.Submitted, submittedAt: Now.AddMinutes(-6));

        var pending = await Channel(transport).DispatchAsync(waiting, SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);
        var unconfirmed = await Channel(transport).DispatchAsync(expired, SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        transport.Requests.Should().BeEmpty();
        pending.Retryable.Should().BeTrue();
        pending.Events.Should().BeEmpty();
        unconfirmed.Retryable.Should().BeFalse();
        unconfirmed.Events.Should().ContainSingle(item => item.EventType == "failed" && item.FailureCategory == "delivery-unconfirmed");
    }

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBeingRecordedAsAnOutcome()
    {
        var transport = new FakeAcsTransport();
        using var cancellation = new CancellationTokenSource();
        transport.Enqueue(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        var dispatch = () => Channel(transport).DispatchAsync(Request(), SimulationDispatchScenario.ImmediateSuccess, cancellation.Token);

        await dispatch.Should().ThrowAsync<OperationCanceledException>();
    }

    private static AcsSmsOptions Options(
        string environment = "Test",
        string? endpoint = Endpoint,
        string? accessKey = DefaultKey,
        string? fromNumber = FromNumber,
        IReadOnlyDictionary<string, string>? recipients = null)
        => AcsSmsOptions.Create(
            environment,
            endpoint,
            accessKey == DefaultKey ? AccessKey : accessKey,
            fromNumber,
            recipients ?? new Dictionary<string, string> { [Label] = TestNumber },
            deliveryReportWindowSeconds: null,
            uncertainOutcomeWindowSeconds: null);

    private static AzureCommunicationServicesSmsChannel Channel(FakeAcsTransport transport, TimeProvider? clock = null)
        => new(Options(), transport, clock ?? new FixedTime(Now));

    private static NotificationDispatchRequest Request(
        string endpointReference = Label,
        string wakeUpText = "SIMULATION: urgent secure message available.",
        DeliveryAttemptStatus status = DeliveryAttemptStatus.Requested,
        DateTimeOffset? submittedAt = null)
        => new(
            new OrganizationId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            new AlertId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            new AlertDraftVersion(3),
            new AlertRecipientSelectionId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            NotificationChannel.Sms,
            endpointReference,
            "alert:22222222222222222222222222222222:v3",
            wakeUpText,
            "alert-dispatch:22222222222222222222222222222222:v3:r33333333333333333333333333333333:c1:a1",
            "dispatch:44444444444444444444444444444444",
            status,
            AttemptRequestedAtUtc: Now,
            SubmittedAtUtc: submittedAt);

    private static Func<HttpRequestMessage, HttpResponseMessage> Accepted(string messageId, string? repeatability = null)
        => _ => Json(HttpStatusCode.Accepted, JsonSerializer.Serialize(new
        {
            value = new object[]
            {
                repeatability is null
                    ? new { to = TestNumber, messageId, httpStatusCode = 202, successful = true }
                    : new { to = TestNumber, messageId, httpStatusCode = 202, repeatabilityResult = repeatability, successful = true },
            },
        }));

    private static Func<HttpRequestMessage, HttpResponseMessage> Item(int status, bool successful, string? messageId)
        => _ => Json(HttpStatusCode.Accepted, JsonSerializer.Serialize(new
        {
            value = new[] { new { to = TestNumber, messageId, httpStatusCode = status, successful, errorMessage = $"{TestNumber} detail" } },
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>Fake ACS SMS endpoint that verifies the documented HMAC scheme with its own implementation.</summary>
public sealed class FakeAcsTransport : HttpMessageHandler
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

    public List<RecordedAcsRequest> Requests { get; } = [];

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) => responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var date = request.Headers.TryGetValues("x-ms-date", out var dates) ? dates.Single() : string.Empty;
        var hash = request.Headers.TryGetValues("x-ms-content-sha256", out var hashes) ? hashes.Single() : string.Empty;
        var host = request.RequestUri!.Authority;
        var stringToSign = $"{request.Method.Method}\n{request.RequestUri.PathAndQuery}\n{date};{host};{hash}";
        var expected = Convert.ToBase64String(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(stringToSign)));
        var authorization = request.Headers.Authorization?.ToString() ?? string.Empty;
        var bodyHashValid = hash == Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        string? repeatabilityId = null;
        string? firstSent = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            var recipient = json.RootElement.GetProperty("smsRecipients")[0];
            repeatabilityId = recipient.GetProperty("repeatabilityRequestId").GetString();
            firstSent = recipient.GetProperty("repeatabilityFirstSent").GetString();
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
        }

        Requests.Add(new RecordedAcsRequest(
            request.RequestUri.PathAndQuery,
            body,
            bodyHashValid && authorization == $"HMAC-SHA256 SignedHeaders=x-ms-date;host;x-ms-content-sha256&Signature={expected}"
                && DateTimeOffset.TryParse(date, out _),
            repeatabilityId,
            firstSent));
        var next = responses.Count > 0
            ? responses.Dequeue()
            : _ => new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(
                    $"{{\"value\":[{{\"to\":\"+15555550142\",\"messageId\":\"acs-fake-{Requests.Count:D4}\",\"httpStatusCode\":202,\"successful\":true}}]}}",
                    Encoding.UTF8,
                    "application/json"),
            };
        return next(request);
    }
}

public sealed record RecordedAcsRequest(
    string PathAndQuery,
    string Body,
    bool SignatureValid,
    string? RepeatabilityRequestId,
    string? RepeatabilityFirstSent);
