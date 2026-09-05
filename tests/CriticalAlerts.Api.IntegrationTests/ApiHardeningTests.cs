using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CriticalAlerts.Application.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class ApiHardeningTests
{
    [Fact]
    public async Task RuntimeOpenApiIsExplicitlyVersionedAsThreeOne()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        document.RootElement.GetProperty("openapi").GetString().Should().StartWith("3.1.");
        document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/alerts/{alertId}/resolve", out _).Should().BeTrue();
        document.RootElement.GetProperty("paths").TryGetProperty("/api/alerts/{alertId}/resolve", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ApiRequestBodyLimitRejectsOversizedRequests()
    {
        await using var factory = CreateFactory(developmentAuthenticationEnabled: true);
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(new byte[(2 * 1024 * 1024) + 1]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync("/api/v1/dev/session", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task ApiRateLimiterReturnsTooManyRequestsAfterTheWindowBudget()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 130).Select(_ => client.GetAsync("/api/v1/me")));

        responses.Should().Contain(response => response.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task UnversionedApiRouteIsNotMapped()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/me");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, null, "192.0.2.1", null, null, "192.0.2.2")]
    [InlineData(null, null, "192.0.2.1", "org-a", "user-a", "192.0.2.1")]
    [InlineData("org-a", "user-a", "192.0.2.1", "org-a", "user-b", "192.0.2.1")]
    [InlineData("org-a", "user-a", "192.0.2.1", "org-b", "user-a", "192.0.2.1")]
    public async Task ExhaustedCallerDoesNotThrottleAnotherCaller(
        string? firstOrganization, string? firstUser, string firstAddress,
        string? secondOrganization, string? secondUser, string secondAddress)
    {
        await using var factory = CreateRateLimitFactory();
        for (var request = 0; request < 120; request++)
        {
            var response = await SendRateLimitedRequestAsync(factory, firstOrganization, firstUser, firstAddress);
            response.Response.StatusCode.Should().Be(firstUser is null ? 401 : 200);
        }

        var exhausted = await SendRateLimitedRequestAsync(factory, firstOrganization, firstUser, firstAddress);
        exhausted.Response.StatusCode.Should().Be(429);
        var independent = await SendRateLimitedRequestAsync(factory, secondOrganization, secondUser, secondAddress);
        independent.Response.StatusCode.Should().Be(secondUser is null ? 401 : 200);
    }

    [Fact]
    public async Task ForwardedAddressDoesNotResetAnonymousCallerBudget()
    {
        await using var factory = CreateRateLimitFactory();
        for (var request = 0; request < 120; request++)
        {
            await SendRateLimitedRequestAsync(factory, null, null, "192.0.2.1");
        }

        var response = await factory.Server.SendAsync(context =>
        {
            context.Request.Path = "/api/v1/me";
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
            context.Request.Headers["X-Forwarded-For"] = "192.0.2.2";
        });
        response.Response.StatusCode.Should().Be(429);
    }

    private static Task<HttpContext> SendRateLimitedRequestAsync(
        WebApplicationFactory<Program> factory, string? organization, string? user, string address)
        => factory.Server.SendAsync(context =>
        {
            context.Request.Path = "/api/v1/me";
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            if (user is not null)
            {
                context.Items["test-identity"] = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, user), new Claim(AuthenticationClaimTypes.OrganizationId, organization!)],
                    "RateLimitTest"));
            }
        });

    private static WebApplicationFactory<Program> CreateRateLimitFactory()
        => CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddAuthentication("RateLimitTest")
                .AddScheme<AuthenticationSchemeOptions, RateLimitTestAuthenticationHandler>("RateLimitTest", _ => { })));

    private sealed class RateLimitTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(Context.Items["test-identity"] is ClaimsPrincipal principal
                ? AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name))
                : AuthenticateResult.NoResult());
    }

    private static WebApplicationFactory<Program> CreateFactory(bool developmentAuthenticationEnabled = false)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .UseEnvironment("Test")
                .UseSetting("DevelopmentAuthentication:Enabled", developmentAuthenticationEnabled.ToString())
                .UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused")
                .UseSetting("DataProtection:Key", Convert.ToBase64String(new byte[32])));
}
