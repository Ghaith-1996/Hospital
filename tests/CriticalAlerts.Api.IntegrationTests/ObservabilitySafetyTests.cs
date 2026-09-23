using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class ObservabilitySafetyTests
{
    [Fact]
    public async Task AuditRateLimitReturnsSafeCorrelatedProblemDetails()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        for (var index = 0; index < 120; index++)
        {
            using var attempt = await client.GetAsync("/api/v1/admin/audit");
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using var response = await client.GetAsync("/api/v1/admin/audit");
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (response.Content.Headers.ContentType?.MediaType).Should().Be("application/problem+json");
        (response.Headers.RetryAfter?.Delta).Should().NotBeNull();
        response.Headers.RetryAfter!.Delta!.Value.Should().BeGreaterThan(TimeSpan.Zero);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        problem.RootElement.GetProperty("correlationId").GetString().Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("SIM-PATIENT-PHASE10-SENTINEL")]
    [InlineData("phase10@example.invalid")]
    [InlineData("+1-555-PHASE10")]
    [InlineData("SIM-SECRET-PHASE10-SENTINEL")]
    [InlineData("unsafe/value")]
    public async Task EffectiveCorrelationIsOpaqueAndHealthNeverReflectsInput(string? supplied)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        if (supplied is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-ID", supplied);
        using var response = await client.GetAsync("/health/live");
        var effective = response.Headers.GetValues("X-Correlation-ID").Single();
        Guid.TryParse(effective, out _).Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        if (supplied is not null) body.Contains(supplied, StringComparison.Ordinal).Should().BeFalse();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("correlationId").GetString().Should().Be(effective);
    }

    [Theory]
    [InlineData("11111111111141118111111111111111")]
    [InlineData("11111111-1111-4111-8111-111111111111")]
    public async Task SafeClientCorrelationIsPreserved(string supplied)
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", supplied);
        using var response = await client.GetAsync("/health/live");
        response.Headers.GetValues("X-Correlation-ID").Single().Should().Be(supplied);
    }

    [Fact]
    public async Task ActualRequestLogsNeverCaptureQueryOrPayloadAndEmitSafeRejection()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var capture = new CapturingLoggerProvider();
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(capture);
        const string sentinel = "SIM-APPROVED-MESSAGE-DO-NOT-LOG";
        using var response = await client.PostAsJsonAsync("/api/v1/alerts?unexpected=" + sentinel,
            new { patientReference = sentinel, sourceText = sentinel });
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(400);
        var output = string.Join("\n", capture.Entries);
        output.Contains(sentinel, StringComparison.Ordinal).Should().BeFalse();
        output.Should().Contain("ApiRequestRejected");
        var effective = response.Headers.GetValues("X-Correlation-ID").Single();
        output.Should().Contain(effective);
    }

    [Fact]
    public async Task OversizedCorrelationAndBodyAreRejectedSafelyWithCorrelation()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-ID", new string('x', 2048));
        using var content = new ByteArrayContent(new byte[2097153]);
        using var response = await client.PostAsync("/api/v1/dev/session", content);
        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Guid.TryParse(response.Headers.GetValues("X-Correlation-ID").Single(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task ReadinessFailureHasOnlySafeOutputAndOperationalLog()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        using var capture = new CapturingLoggerProvider();
        factory.Services.GetRequiredService<ILoggerFactory>().AddProvider(capture);
        using var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var output = await response.Content.ReadAsStringAsync() + string.Join("\n", capture.Entries);
        output.Contains("SIM-SECRET-PHASE10-SENTINEL", StringComparison.Ordinal).Should().BeFalse();
        output.Contains("Host=", StringComparison.Ordinal).Should().BeFalse();
        output.Should().Contain("DatabaseReadinessFailed");
        response.Headers.Contains("X-Correlation-ID").Should().BeTrue();
    }

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Test")
            .UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Port=1;Timeout=1;Database=unused;Username=unused;Password=SIM-SECRET-PHASE10-SENTINEL")
            .UseSetting("DataProtection:Key", Convert.ToBase64String(new byte[32])));
}
