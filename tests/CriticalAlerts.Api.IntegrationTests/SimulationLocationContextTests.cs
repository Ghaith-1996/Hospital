using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class SimulationLocationContextTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task AnonymousCallerIsRejected()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/v1/dev/location-context");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PractitionerRoleIsRejected()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var response = await client.GetAsync("/api/v1/dev/location-context");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperatorReceivesOnlyOrganizationScopedSitesAndDepartments()
    {
        var foreign = await fixture.CreateForeignOperatorDraftAsync();
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var response = await client.GetAsync("/api/v1/dev/location-context");
        var context = await response.Content.ReadFromJsonAsync<LocationContextDto>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        context.Should().NotBeNull();
        context!.OrganizationId.Should().Be(DemoDataSeeder.OrganizationId.Value.ToString("D"));
        context.Sites.Select(site => site.Name).Should().Equal(
            "North Wing Simulation Site",
            "Riverside Annex Simulation Site");
        context.Sites.SelectMany(site => site.Departments).Select(department => department.Name).Should().Equal(
            "Fictional Emergency Care",
            "Fictional Medicine",
            "Fictional Surgery");
        context.Sites.Select(site => Guid.Parse(site.SiteId)).Should().OnlyContain(id => id != Guid.Empty);
        context.Sites.SelectMany(site => site.Departments).Select(department => Guid.Parse(department.DepartmentId))
            .Should().OnlyContain(id => id != Guid.Empty);
        context.Sites.Select(site => site.Name).Should().NotContain("Fictional Foreign Simulation Site");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task EndpointIsUnavailableWhenDevelopmentAuthenticationIsDisabled(string environment)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("DevelopmentAuthentication:Enabled", "false");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("DataProtection:Key", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/dev/location-context");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private sealed record LocationContextDto(string OrganizationId, SiteDto[] Sites);

    private sealed record SiteDto(string SiteId, string Name, DepartmentDto[] Departments);

    private sealed record DepartmentDto(string DepartmentId, string Name);
}
