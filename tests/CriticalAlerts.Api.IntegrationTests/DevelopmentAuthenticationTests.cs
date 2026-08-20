using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class DevelopmentAuthenticationTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedMeReturnsProblemDetails()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/me");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("authentication-required");
        body.Should().NotContain("Host=");
    }

    [Fact]
    public async Task SessionUsesSeededHandleNotArbitraryUserId()
    {
        using var client = fixture.CreateClient();

        using var unknown = await client.PostAsJsonAsync("/api/dev/session", new { simulationHandle = fixture.JordanUserId.ToString("D") });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var created = await client.PostAsJsonAsync("/api/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle });
        created.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        spoof.Headers.Add("X-User-Id", DemoDataSeeder.MorganUserId.Value.ToString("D"));
        spoof.Headers.Add("X-Organization-Id", Guid.NewGuid().ToString("D"));
        using var me = await client.SendAsync(spoof);
        var body = await me.Content.ReadFromJsonAsync<CurrentUserDto>();

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.SimulationHandle.Should().Be(DemoDataSeeder.JordanHandle);
        body.UserId.Should().Be(DemoDataSeeder.JordanUserId.Value.ToString("D"));
        body.Roles.Should().Equal("Operator");
        body.DevelopmentAuthentication.Should().BeTrue();
    }

    [Fact]
    public async Task OperatorCannotUseAdministratorEndpoint()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var operatorProbe = await client.GetAsync("/api/authorization/operator");
        using var administratorProbe = await client.GetAsync("/api/authorization/administrator");

        operatorProbe.StatusCode.Should().Be(HttpStatusCode.NoContent);
        administratorProbe.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdministratorAndPractitionerRolesAreDistinct()
    {
        using var administrator = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        (await administrator.GetAsync("/api/authorization/administrator")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await administrator.GetAsync("/api/authorization/operator")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await practitioner.GetAsync("/api/authorization/practitioner")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await practitioner.GetAsync("/api/authorization/administrator")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrganizationScopeRejectsForeignOrganization()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        (await client.GetAsync($"/api/authorization/organization-scope/{DemoDataSeeder.OrganizationId.Value:D}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/authorization/organization-scope/{Guid.NewGuid():D}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IdentitySwitcherListsHandlesNotArbitraryIds()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/dev/identities");
        var identities = await response.Content.ReadFromJsonAsync<DevelopmentIdentityDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        identities.Should().NotBeNull();
        identities!.Select(identity => identity.SimulationHandle).Should().BeEquivalentTo(
        [
            DemoDataSeeder.JordanHandle,
            DemoDataSeeder.MorganHandle,
            DemoDataSeeder.RileyHandle,
        ]);
        identities.Should().OnlyContain(identity => identity.DisplayName.Length > 0);
    }

    [Fact]
    public void ProductionCannotEnableDevelopmentAuthentication()
    {
        var act = () =>
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
                builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            });
            using var client = factory.CreateClient();
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
    }

    private sealed record CurrentUserDto(
        string UserId,
        string OrganizationId,
        string DisplayName,
        string SimulationHandle,
        string[] Roles,
        bool DevelopmentAuthentication);

    private sealed record DevelopmentIdentityDto(
        string DisplayName,
        string SimulationHandle,
        string[] Roles,
        string OrganizationId);
}

public sealed class SeededPostgresApiFixture : IAsyncLifetime
{
    private readonly PostgresApiFixture inner = new();
    private string dataProtectionKey = string.Empty;
    private WebApplicationFactory<Program>? factory;

    public Guid JordanUserId => DemoDataSeeder.JordanUserId.Value;

    public async Task InitializeAsync()
    {
        await inner.InitializeAsync();
        dataProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", inner.ConnectionString);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
        });
    }

    public HttpClient CreateClient()
        => factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public async Task<HttpClient> CreateSignedInClientAsync(string simulationHandle)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/dev/session", new { simulationHandle });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await inner.DisposeAsync();
    }
}

[CollectionDefinition(SeededPostgresApiCollection.Name)]
public sealed class SeededPostgresApiCollection : ICollectionFixture<SeededPostgresApiFixture>
{
    public const string Name = "postgres-api-seeded";
}
