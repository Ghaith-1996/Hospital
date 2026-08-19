using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task LiveHealthDoesNotRequireDatabase()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadyHealthReportsDatabaseFailureSafely()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        body.Should().Contain("Unhealthy");
        body.Should().NotContain("Host=");
        body.Should().NotContain("127.0.0.1");
        body.Should().NotContain("SELECT");
    }
}

[Collection(PostgresApiCollection.Name)]
public sealed class HealthyHealthEndpointsTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task ReadyHealthReportsHealthyWhenDatabaseIsAvailable()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder
                .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:CriticalAlerts"] = fixture.ConnectionString,
                    })));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
