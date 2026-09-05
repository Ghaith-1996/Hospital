using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class DevelopmentAuthenticationTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedMeReturnsProblemDetails()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/me");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("authentication-required");
        body.Should().NotContain("Host=");
    }

    [Fact]
    public async Task SessionUsesSeededHandleNotArbitraryUserId()
    {
        using var client = fixture.CreateClient();

        using var unknown = await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = fixture.JordanUserId.ToString("D") });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var created = await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle });
        created.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        spoof.Headers.Add("X-User-Id", DemoDataSeeder.MorganUserId.Value.ToString("D"));
        spoof.Headers.Add("X-Organization-Id", Guid.NewGuid().ToString("D"));
        using var me = await client.SendAsync(spoof);
        var body = await me.Content.ReadFromJsonAsync<CurrentUserDto>();

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.SimulationHandle.Should().Be(DemoDataSeeder.JordanHandle);
        body.UserId.Should().Be(DemoDataSeeder.JordanUserId.Value.ToString("D"));
        body.OrganizationId.Should().Be(DemoDataSeeder.OrganizationId.Value.ToString("D"));
        body.Roles.Should().Equal("Operator");
        body.DevelopmentAuthentication.Should().BeTrue();
    }

    [Fact]
    public async Task ClientSuppliedRolesAreIgnored()
    {
        using var client = fixture.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/v1/dev/session", new
        {
            simulationHandle = DemoDataSeeder.JordanHandle,
            roles = new[] { "Administrator", "Practitioner" },
            organizationId = Guid.NewGuid(),
            userId = DemoDataSeeder.MorganUserId.Value,
        });
        created.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var me = await client.GetAsync("/api/v1/me");
        var body = await me.Content.ReadFromJsonAsync<CurrentUserDto>();

        body!.Roles.Should().Equal("Operator");
        body.UserId.Should().Be(DemoDataSeeder.JordanUserId.Value.ToString("D"));
        body.OrganizationId.Should().Be(DemoDataSeeder.OrganizationId.Value.ToString("D"));
    }

    [Fact]
    public async Task UnauthenticatedProtectedAuthorizationReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/authorization/operator");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("authentication-required");
    }

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle, "/api/v1/authorization/operator", HttpStatusCode.NoContent)]
    [InlineData(DemoDataSeeder.MorganHandle, "/api/v1/authorization/administrator", HttpStatusCode.NoContent)]
    [InlineData(DemoDataSeeder.RileyHandle, "/api/v1/authorization/practitioner", HttpStatusCode.NoContent)]
    public async Task SeededRoleHasPositiveAuthorization(string handle, string path, HttpStatusCode expected)
    {
        using var client = await fixture.CreateSignedInClientAsync(handle);
        (await client.GetAsync(path)).StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle, "/api/v1/authorization/administrator")]
    [InlineData(DemoDataSeeder.JordanHandle, "/api/v1/authorization/practitioner")]
    [InlineData(DemoDataSeeder.MorganHandle, "/api/v1/authorization/operator")]
    [InlineData(DemoDataSeeder.MorganHandle, "/api/v1/authorization/practitioner")]
    [InlineData(DemoDataSeeder.RileyHandle, "/api/v1/authorization/operator")]
    [InlineData(DemoDataSeeder.RileyHandle, "/api/v1/authorization/administrator")]
    public async Task SeededRoleIsDeniedOtherAuthorization(string handle, string path)
    {
        using var client = await fixture.CreateSignedInClientAsync(handle);
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperatorCannotUseAdministratorEndpoint()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var operatorProbe = await client.GetAsync("/api/v1/authorization/operator");
        using var administratorProbe = await client.GetAsync("/api/v1/authorization/administrator");

        operatorProbe.StatusCode.Should().Be(HttpStatusCode.NoContent);
        administratorProbe.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdministratorAndPractitionerRolesAreDistinct()
    {
        using var administrator = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        (await administrator.GetAsync("/api/v1/authorization/administrator")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await administrator.GetAsync("/api/v1/authorization/operator")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await practitioner.GetAsync("/api/v1/authorization/practitioner")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await practitioner.GetAsync("/api/v1/authorization/administrator")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrganizationScopeRejectsForeignOrganization()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        (await client.GetAsync($"/api/v1/authorization/organization-scope/{DemoDataSeeder.OrganizationId.Value:D}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/authorization/organization-scope/{Guid.NewGuid():D}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IdentitySwitcherListsHandlesNotArbitraryIds()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/dev/identities");
        var identities = await response.Content.ReadFromJsonAsync<DevelopmentIdentityDto[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        identities.Should().NotBeNull();
        var handles = identities!.Select(identity => identity.SimulationHandle);
        handles.Should().Contain(DemoDataSeeder.JordanHandle);
        handles.Should().Contain(DemoDataSeeder.MorganHandle);
        handles.Should().Contain(DemoDataSeeder.RileyHandle);
        identities.Should().OnlyContain(identity => identity.DisplayName.Length > 0);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonSimulationEnvironmentsCannotEnableDevelopmentAuthentication(string environment)
    {
        var act = () =>
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
                builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            });
            using var client = factory.CreateClient();
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task DevelopmentSwitcherIsUnavailableWhenDisabled(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        });
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/v1/dev/identities")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
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
    private readonly CapturingLoggerProvider logProvider = new();
    private string dataProtectionKey = string.Empty;
    private WebApplicationFactory<Program>? factory;

    public Guid JordanUserId => DemoDataSeeder.JordanUserId.Value;

    public string DataProtectionKey => dataProtectionKey;

    public CriticalAlertsDbContext CreateContext()
        => DatabaseOperations.CreateContext(inner.ConnectionString);

    public IReadOnlyList<string> LogEntries => logProvider.Entries;

    public async Task InitializeAsync()
    {
        await inner.InitializeAsync();
        dataProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", inner.ConnectionString);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
            builder.UseSetting("SimulationResponses:Enabled", "true");
            builder.UseSetting("DataProtection:Key", dataProtectionKey);
            builder.ConfigureTestServices(services =>
            {
                // This shared host exercises authorization and workflows across many tests.
                // ApiHardeningTests verifies the real request budgets with isolated hosts.
                services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
                services.Configure<RateLimiterOptions>(options => options.AddPolicy("api", _ => RateLimitPartition.GetNoLimiter("test-workflows")));
            });
        });
        factory.Server.Services.GetRequiredService<ILoggerFactory>().AddProvider(logProvider);
    }

    public HttpClient CreateClient()
        => factory!.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    public async Task<HttpClient> CreateSignedInClientAsync(string simulationHandle)
    {
        var client = CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle });
        response.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<Guid> CreateForeignDraftAsync()
        => (await CreateForeignOperatorDraftAsync()).AlertId;

    public async Task<ForeignOperatorDraftFixture> CreateForeignOperatorDraftAsync()
    {
        var now = DateTimeOffset.Parse("2026-08-01T12:00:00Z");
        var organizationId = OrganizationId.New();
        var siteId = SiteId.New();
        var departmentId = DepartmentId.New();
        var userId = UserId.New();
        var roleId = RoleId.New();
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var simulationHandle = $"sim-foreign-operator-{uniqueSuffix}";
        var protector = AesGcmSensitiveDataProtector.FromBase64(dataProtectionKey);
        var options = new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(inner.ConnectionString)
            .Options;
        await using var db = new CriticalAlertsDbContext(options);

        db.Organizations.Add(Organization.CreateSimulation(
            organizationId,
            $"Fictional Cross-Organization Simulation Hospital {uniqueSuffix}",
            now));
        db.Sites.Add(Site.Create(
            siteId,
            organizationId,
            "Fictional Foreign Simulation Site",
            "SIM-SITE-FOREIGN",
            now));
        db.Departments.Add(Department.Create(
            departmentId,
            organizationId,
            siteId,
            "Fictional Foreign Simulation Department",
            "SIM-DEPT-FOREIGN",
            now));
        db.Users.Add(UserAccount.CreateSimulation(
            userId,
            organizationId,
            "Foreign Simulation Operator",
            simulationHandle,
            now));
        db.Roles.Add(Role.Create(roleId, organizationId, "Operator"));
        db.UserRoles.Add(UserRole.Create(organizationId, userId, roleId));

        var alert = Alert.CreateDraft(
            AlertId.New(),
            organizationId,
            siteId,
            departmentId,
            userId,
            "SIM-PAT-FOREIGN",
            protector.Protect(
                "SIM-PAT-FOREIGN",
                new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, organizationId.Value)),
            "Foreign Simulation Room",
            "Urgent",
            AlertSourceType.Typed,
            protector.Protect(
                "SIMULATION: foreign fictional typed source",
                new SensitiveDataContext("alert-typed-source", organizationId.Value)),
            now,
            protector.Protect(
                "{\"situation\":\"SIMULATION: foreign fictional situation\",\"background\":\"SIMULATION: foreign fictional background\",\"assessment\":\"SIMULATION: foreign fictional assessment\",\"recommendation\":\"SIMULATION: foreign fictional recommendation\"}",
                new SensitiveDataContext("alert-sbar", organizationId.Value)));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return new ForeignOperatorDraftFixture(alert.Id.Value, simulationHandle);
    }

    public async Task<string> GetAlertStateAsync(Guid alertId)
    {
        var options = new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(inner.ConnectionString)
            .Options;
        await using var db = new CriticalAlertsDbContext(options);
        var alert = await db.Alerts.AsNoTracking().SingleAsync(candidate => candidate.Id == new AlertId(alertId));
        return alert.State.ToString();
    }

    public void ClearLogs() => logProvider.Clear();

    public async Task DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }

        await inner.DisposeAsync();
    }
}

public sealed record ForeignOperatorDraftFixture(Guid AlertId, string SimulationHandle);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> entries = new();

    public IReadOnlyList<string> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void Clear()
    {
        while (entries.TryDequeue(out _))
        {
        }
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = $"{categoryName} {logLevel} {eventId.Id} {formatter(state, exception)}";
            if (exception is not null)
            {
                message += $" {exception.GetType().Name}: {exception.Message}";
            }

            entries.Enqueue(message);
        }
    }
}

[CollectionDefinition(SeededPostgresApiCollection.Name)]
public sealed class SeededPostgresApiCollection : ICollectionFixture<SeededPostgresApiFixture>
{
    public const string Name = "postgres-api-seeded";
}
