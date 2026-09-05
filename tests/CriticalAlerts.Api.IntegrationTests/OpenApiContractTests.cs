using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task CommittedContractMatchesTheCompleteRuntimeContractSemantically()
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();

        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(FindRepositoryFile("docs", "api", "openapi.json")));

        Canonicalize(runtime).ToJsonString().Should().Be(Canonicalize(committed).ToJsonString());
    }

    [Fact]
    public void SemanticComparisonIgnoresObjectAndSetArrayOrderingButPreservesMeaningfulArrayOrdering()
    {
        var first = JsonNode.Parse("""{"required":["b","a"],"properties":{"b":{"type":"string"},"a":{"type":"string"}},"prefixItems":[{"type":"string"},{"type":"number"}]}""");
        var harmlessReordering = JsonNode.Parse("""{"prefixItems":[{"type":"string"},{"type":"number"}],"properties":{"a":{"type":"string"},"b":{"type":"string"}},"required":["a","b"]}""");
        var semanticChange = JsonNode.Parse("""{"prefixItems":[{"type":"number"},{"type":"string"}],"properties":{"a":{"type":"string"},"b":{"type":"string"}},"required":["a","b"]}""");

        Canonicalize(first).ToJsonString().Should().Be(Canonicalize(harmlessReordering).ToJsonString());
        Canonicalize(first).ToJsonString().Should().NotBe(Canonicalize(semanticChange).ToJsonString());
    }

    [Fact]
    public async Task LocationContextContractDeclaresSuccessSchemaAndAuthorizationStatuses()
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();

        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        var operation = runtime["paths"]!["/api/v1/dev/location-context"]!["get"]!;

        operation["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/SimulationLocationContextResponse");
        operation["responses"]!["401"].Should().NotBeNull();
        operation["responses"]!["403"].Should().NotBeNull();
    }

    private static WebApplicationFactory<Program> CreateContractHost()
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("DevelopmentAuthentication:Enabled", "true");
            builder.UseSetting("SimulationResponses:Enabled", "true");
            builder.UseSetting("ConnectionStrings:CriticalAlerts", "Host=127.0.0.1;Database=openapi_unused;Username=unused;Password=unused");
            builder.UseSetting("DataProtection:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        });

    private static JsonNode Canonicalize(JsonNode? node, string? propertyName = null)
    {
        if (node is JsonObject jsonObject)
        {
            var result = new JsonObject();
            foreach (var property in jsonObject.OrderBy(property => property.Key, StringComparer.Ordinal))
            {
                result[property.Key] = Canonicalize(property.Value, property.Key);
            }

            return result;
        }

        if (node is JsonArray jsonArray)
        {
            var values = jsonArray.Select(value => Canonicalize(value, propertyName)).ToArray();
            if (propertyName is "required" or "enum" or "type" or "allOf" or "anyOf" or "oneOf" or "tags")
            {
                values = values.OrderBy(value => value.ToJsonString(), StringComparer.Ordinal).ToArray();
            }

            return new JsonArray(values);
        }

        return node?.DeepClone() ?? JsonValue.Create((string?)null)!;
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run inside the repository");
        return Path.Combine([directory!.FullName, .. segments]);
    }
}
