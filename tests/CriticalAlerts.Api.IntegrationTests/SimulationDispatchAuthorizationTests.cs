using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class SimulationDispatchAuthorizationTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedDispatchScenarioAndDeliveryStatusRequestsAreUnauthorized()
    {
        using var client = fixture.CreateClient();

        using var scenario = await client.GetAsync("/api/v1/dev/dispatch-scenarios/Sms");
        using var status = await client.GetAsync($"/api/v1/alerts/{Guid.NewGuid():D}/delivery");

        scenario.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        status.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle, HttpStatusCode.Forbidden)]
    [InlineData(DemoDataSeeder.RileyHandle, HttpStatusCode.Forbidden)]
    public async Task OnlyAdministratorsCanChangeSimulationScenarios(string handle, HttpStatusCode expected)
    {
        using var client = await fixture.CreateSignedInClientAsync(handle);

        using var response = await client.PutAsJsonAsync(
            "/api/v1/dev/dispatch-scenarios/Sms",
            new
            {
                scenario = "ProviderOutage",
                organizationId = Guid.NewGuid(),
                userId = DemoDataSeeder.MorganUserId.Value,
                role = "Administrator",
            });

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task AdministratorScenarioControlDerivesOrganizationAndUserFromAuthentication()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);

        using var set = await client.PutAsJsonAsync(
            "/api/v1/dev/dispatch-scenarios/Sms",
            new
            {
                scenario = "ProviderOutage",
                organizationId = Guid.NewGuid(),
                userId = DemoDataSeeder.JordanUserId.Value,
                roles = new[] { "Operator" },
            });
        using var get = await client.GetAsync("/api/v1/dev/dispatch-scenarios/Sms");
        var value = await get.Content.ReadFromJsonAsync<ScenarioDto>();

        set.StatusCode.Should().Be(HttpStatusCode.OK);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        value.Should().Be(new ScenarioDto("Sms", "ProviderOutage"));

        using var reset = await client.DeleteAsync("/api/v1/dev/dispatch-scenarios/Sms");
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ScenarioControlRejectsUnknownValuesWithoutRevealingConfiguration()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);

        using var response = await client.PutAsJsonAsync(
            "/api/v1/dev/dispatch-scenarios/Voice",
            new { scenario = "RealProviderSecret" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("scenario-invalid");
        body.Should().NotContain("RealProviderSecret");
        body.Should().NotContain("Administrator");
    }

    [Fact]
    public async Task PractitionerCannotReadDeliveryStatusAndOperatorsCannotReadOtherOrganizations()
    {
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var practitionerResponse = await practitioner.GetAsync($"/api/v1/alerts/{Guid.NewGuid():D}/delivery");

        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var operatorResponse = await operatorClient.GetAsync($"/api/v1/alerts/{Guid.NewGuid():D}/delivery");

        practitionerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        operatorResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProductionDoesNotExposeSimulationScenarioControls()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/api/v1/dev/dispatch-scenarios/Sms");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record ScenarioDto(string Channel, string Scenario);
}
