using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class DirectoryAuthorizationAndImportTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedDirectorySearchReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync("/api/directory/practitioners");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("authentication-required");
    }

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle, HttpStatusCode.OK)]
    [InlineData(DemoDataSeeder.MorganHandle, HttpStatusCode.OK)]
    [InlineData(DemoDataSeeder.RileyHandle, HttpStatusCode.Forbidden)]
    public async Task DirectorySearchAuthorizationMatchesSeededRoles(string handle, HttpStatusCode expected)
    {
        using var client = await fixture.CreateSignedInClientAsync(handle);
        (await client.GetAsync("/api/directory/practitioners?q=Martin")).StatusCode.Should().Be(expected);
    }

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle)]
    [InlineData(DemoDataSeeder.RileyHandle)]
    public async Task NonAdministratorsCannotImportTheDirectory(string handle)
    {
        using var client = await fixture.CreateSignedInClientAsync(handle);
        using var content = CsvContent(File.ReadAllText(FixturePath()));

        (await client.PostAsync("/api/directory/imports/preview", content)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdministratorPreviewDoesNotPersistCsvAdapterRecords()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var before = await client.GetAsync("/api/directory/practitioners");
        var beforeBody = await before.Content.ReadFromJsonAsync<DirectoryPractitionerListItem[]>();

        using var content = CsvContent(File.ReadAllText(FixturePath()));
        using var preview = await client.PostAsync("/api/directory/imports/preview", content);
        var previewBody = await preview.Content.ReadFromJsonAsync<DirectoryImportPreviewResult>();

        using var after = await client.GetAsync("/api/directory/practitioners");
        var afterBody = await after.Content.ReadFromJsonAsync<DirectoryPractitionerListItem[]>();
        using var martins = await client.GetAsync("/api/directory/practitioners?q=Martin");
        var martinBody = await martins.Content.ReadFromJsonAsync<DirectoryPractitionerListItem[]>();

        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        previewBody!.Errors.Should().BeEmpty();
        previewBody.UpdateCount.Should().Be(12);
        afterBody.Should().HaveCount(beforeBody!.Length);
        afterBody!.Single(item => item.SimulationCode == "SIM-PRAC-0111").Selectable.Should().BeFalse();
        martinBody.Should().HaveCount(2);
        martinBody.Should().OnlyContain(item => item.LastName == "Martin");
    }

    [Fact]
    public async Task AdministratorCanApplyTheSimulationCsv()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        using var content = CsvContent(File.ReadAllText(FixturePath()));

        using var response = await client.PostAsync("/api/directory/imports", content);
        var body = await response.Content.ReadFromJsonAsync<DirectoryImportApplyResult>();
        using var search = await client.GetAsync("/api/directory/practitioners?q=Martin");
        var martins = await search.Content.ReadFromJsonAsync<DirectoryPractitionerListItem[]>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body!.Applied.Should().BeTrue();
        body.Preview.UpdateCount.Should().Be(12);
        martins.Should().HaveCount(2);
        martins.Should().OnlyContain(item => item.SourceSystem == DirectorySourceSystems.Csv);
    }

    private static MultipartFormDataContent CsvContent(string csv)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "directory-harborview.csv");
        return content;
    }

    private static string FixturePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "fixtures", "simulation", "directory-harborview.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate fixtures/simulation/directory-harborview.csv.");
    }
}
