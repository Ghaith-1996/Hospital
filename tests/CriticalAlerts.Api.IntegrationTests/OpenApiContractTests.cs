using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Theory]
    [InlineData("/api/v1/alerts/drafts", "post", "201", "AlertDraftView")]
    [InlineData("/api/v1/alerts/{alertId}", "get", "200", "AlertDraftView")]
    [InlineData("/api/v1/alerts/{alertId}/review", "get", "200", "AlertReviewView")]
    [InlineData("/api/v1/alerts/{alertId}/confirm", "post", "200", "ConfirmAlertReviewResult")]
    [InlineData("/api/v1/alerts/{alertId}/live", "get", "200", "AlertLiveView")]
    [InlineData("/api/v1/my-alerts/{alertId}", "get", "200", "MyAlertDetailView")]
    [InlineData("/api/v1/my-alerts/{alertId}/responses", "post", "200", "RecipientResponseResult")]
    [InlineData("/api/v1/alerts/{alertId}/resolve", "post", "200", "AlertLifecycleResult")]
    public async Task WorkflowResponsesDeclareActualDtoAndSuccessStatus(string path, string method, string status, string schema)
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        var responses = runtime["paths"]![path]![method]!["responses"]!;
        var schemaReference = responses[status]?["content"]?["application/json"]?["schema"]?["$ref"]?.GetValue<string>();
        schemaReference.Should().Be($"#/components/schemas/{schema}");
        responses["401"].Should().NotBeNull();
        responses["403"].Should().NotBeNull();
    }

    [Fact]
    public async Task EveryGeneratedOperationDeclaresAConcreteSuccessSchemaOrNoContentStatus()
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        foreach (var path in runtime["paths"]!.AsObject())
        {
            foreach (var method in path.Value!.AsObject())
            {
                var responses = method.Value!["responses"]!.AsObject();
                var successes = responses.Where(response => response.Key.StartsWith('2')).ToArray();
                successes.Should().ContainSingle($"{method.Key} {path.Key} has one declared success shape");
                var success = successes.Single();
                if (success.Key == "204")
                {
                    success.Value!["content"].Should().BeNull();
                }
                else
                {
                    var successSchema = success.Value?["content"]?["application/json"]?["schema"];
                    successSchema.Should().NotBeNull($"{method.Key} {path.Key} must detect response DTO drift");
                }
            }
        }
        runtime["components"]!["schemas"]!["MyAlertCriticalFieldView"]!["properties"]!["value"].Should().NotBeNull();
        runtime["paths"]!["/api/v1/dev/session"]!["post"]!["responses"]!["204"].Should().NotBeNull();
    }

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
        var schemas = runtime["components"]!["schemas"]!;
        schemas["SimulationLocationContextResponse"]!["properties"]!["organizationId"]!["format"]!.GetValue<string>().Should().Be("uuid");
        schemas["SimulationSiteResponse"]!["properties"]!["siteId"]!["format"]!.GetValue<string>().Should().Be("uuid");
        schemas["SimulationDepartmentResponse"]!["properties"]!["departmentId"]!["format"]!.GetValue<string>().Should().Be("uuid");
        schemas["SimulationSiteResponse"]!["properties"]!["departments"]!["items"]!["$ref"]!.GetValue<string>()
            .Should().Be("#/components/schemas/SimulationDepartmentResponse");
    }

    [Theory]
    [InlineData("/api/v1/alerts/{alertId}/confirm")]
    [InlineData("/api/v1/alerts/{alertId}/resolve")]
    [InlineData("/api/v1/alerts/{alertId}/cancel")]
    [InlineData("/api/v1/my-alerts/{alertId}/opened")]
    [InlineData("/api/v1/my-alerts/{alertId}/responses")]
    public async Task IdempotentCommandsDeclareRequiredIdempotencyHeader(string path)
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;

        var header = runtime["paths"]![path]!["post"]!["parameters"]!.AsArray()
            .Single(parameter => parameter!["name"]!.GetValue<string>() == "Idempotency-Key")!;

        header["in"]!.GetValue<string>().Should().Be("header");
        header["required"]!.GetValue<bool>().Should().BeTrue();
        header["schema"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Theory]
    [InlineData("/api/v1/directory/imports/preview", false)]
    [InlineData("/api/v1/directory/imports", true)]
    public async Task DirectoryImportsDeclareMultipartFileContract(string path, bool requiresPreviewToken)
    {
        using var factory = CreateContractHost();
        using var client = factory.CreateClient();
        var runtime = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;

        var schema = runtime["paths"]![path]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
        var formSchema = schema;

        formSchema["properties"]!["file"]!["format"]!.GetValue<string>().Should().Be("binary");
        formSchema["required"]!.AsArray().Select(value => value!.GetValue<string>()).Should().Contain("file");
        if (requiresPreviewToken)
        {
            formSchema["properties"]!["preview_token"]!["type"]!.GetValue<string>().Should().Be("string");
            formSchema["required"]!.AsArray().Select(value => value!.GetValue<string>()).Should().Contain("preview_token");
        }
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
