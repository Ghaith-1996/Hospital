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
        using (var unknown = await fixture.PostReportsAsync(fixture.ValidToken(), Report("evt-unknown", "acs-not-ours-0001", "Delivered", tag)))
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

        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0);
        (await db.AuditEvents.CountAsync(row => row.ActorType == "provider-webhook")).Should().Be(0);
        fixture.Record("webhook-authentication", results.ToDictionary(item => item.Key, item => (int)item.Value));
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
        async Task Check(string name, HttpContent content, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, AcsSmsEndToEndFixture.WebhookPath) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.ValidToken());
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

        await using var db = fixture.CreateContext();
        (await db.InboxMessages.CountAsync()).Should().Be(0, "no event of a rejected batch may be applied");
        (await fixture.SingleAttemptAsync(alertId)).Status.Should().Be(DeliveryAttemptStatus.Submitted);
        fixture.Record("webhook-validation", results);
    }

    [Fact]
    public async Task SubscriptionValidationEchoesTheCodeOnlyWhenAuthenticated()
    {
        var validation = new[]
        {
            new
            {
                id = "evt-validation",
                topic = "/subscriptions/x/resourceGroups/y/providers/Microsoft.EventGrid/systemTopics/z",
                subject = string.Empty,
                eventType = "Microsoft.EventGrid.SubscriptionValidationEvent",
                eventTime = DateTime.UtcNow.ToString("O"),
                dataVersion = "2",
                data = new { validationCode = "SIM-VALIDATION-CODE-0001" },
            },
        };

        using var anonymous = await fixture.PostReportsAsync(null, validation);
        using var authenticated = await fixture.PostReportsAsync(fixture.ValidToken(), validation);
        var echoed = await authenticated.Content.ReadFromJsonAsync<JsonElement>();

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        echoed.GetProperty("validationResponse").GetString().Should().Be("SIM-VALIDATION-CODE-0001");
        fixture.Record("subscription-validation", new { anonymous = (int)anonymous.StatusCode, authenticated = (int)authenticated.StatusCode });
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

    public SigningFakeAcs Transport { get; private set; } = new(AccessKey);

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

    public async Task ResetAsync()
    {
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true);
        Clock = new MutableClock(DateTimeOffset.UtcNow);
        Transport = new SigningFakeAcs(AccessKey);
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
        RsaSecurityKey? signingKey = null)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(10);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Audience,
            IssuedAt = expiry.AddMinutes(-20),
            NotBefore = expiry.AddMinutes(-20),
            Expires = expiry,
            Claims = new Dictionary<string, object> { ["roles"] = new[] { role }, ["appid"] = "sim-event-grid" },
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
        lock (issuedTokens) issuedTokens.Add(token);
        return token;
    }

    public async Task<HttpResponseMessage> PostReportsAsync(string? token, object events, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(JsonSerializer.Serialize(events), Encoding.UTF8, "application/json"),
        };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await (client ?? Client).SendAsync(request);
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

    public async Task<DispatchProcessingResult> ProcessAsync()
    {
        await using var db = CreateContext();
        // Not disposed: the shared fake transport outlives each worker pass, like a pooled handler.
        var channel = new AzureCommunicationServicesSmsChannel(
            AcsSmsOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(AcsSettings()).Build(), "Test")!,
            Transport,
            Clock);
        var processor = new OutboxDispatchProcessor(
            db,
            [new SimulationSecureMessageChannel(Clock), channel, new SimulationVoiceChannel(Clock)],
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
        return await processor.ProcessNextAsync("phase12-e2e-worker", CancellationToken.None);
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

public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan duration) => now = now.Add(duration);
}

/// <summary>Fake ACS SMS endpoint: verifies the documented HMAC scheme independently and issues opaque message IDs.</summary>
public sealed class SigningFakeAcs(string accessKey) : HttpMessageHandler
{
    private readonly byte[] key = Convert.FromBase64String(accessKey);
    private readonly ConcurrentQueue<HttpStatusCode> scripted = new();

    public ConcurrentQueue<FakeAcsRequest> RequestLog { get; } = new();

    public IReadOnlyList<FakeAcsRequest> Requests => RequestLog.ToArray();

    public void Enqueue(HttpStatusCode status) => scripted.Enqueue(status);

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
        return new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                $"{{\"value\":[{{\"to\":\"{recipient.GetProperty("to").GetString()}\",\"messageId\":\"acs-e2e-{RequestLog.Count:D4}\",\"httpStatusCode\":202,\"repeatabilityResult\":\"accepted\",\"successful\":true}}]}}",
                Encoding.UTF8,
                "application/json"),
        };
    }
}

public sealed record FakeAcsRequest(bool SignatureValid, string RepeatabilityRequestId, string RepeatabilityFirstSent);
