using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AssistanceBoundaryTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task CrossOrganizationAndCrossAlertResultsCannotBeReadOrApplied()
    {
        using var host = fixture.WithAssistance(true);
        using var client = host.CreateClient();
        await SignIn(client, DemoDataSeeder.JordanHandle);
        var draft = await AssistanceApiTests.Create(client);
        var other = await AssistanceApiTests.Create(client);
        using var generated = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1);
        var result = (await generated.Content.ReadFromJsonAsync<AssistanceResultView>())!;
        using var wrongAlert = await AssistanceApiTests.Post(client, other.AlertId, $"structuring-suggestions/{result.Id}/apply", 1);
        wrongAlert.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var foreign = await fixture.CreateForeignOperatorDraftAsync();
        await SignIn(client, foreign.SimulationHandle);
        using var history = await client.GetAsync($"/api/v1/alerts/{draft.AlertId}/structuring-suggestions");
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var apply = await AssistanceApiTests.Post(client, draft.AlertId, $"structuring-suggestions/{result.Id}/apply", 1);
        apply.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var generate = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1);
        generate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HistoryIsBoundedAndCursorHasNoDuplicates()
    {
        using var host = fixture.WithAssistance(true); using var client = host.CreateClient();
        await SignIn(client, DemoDataSeeder.JordanHandle);
        var draft = await AssistanceApiTests.Create(client);
        for (var i = 0; i < 21; i++) (await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1)).EnsureSuccessStatusCode();
        var path = $"/api/v1/alerts/{draft.AlertId}/structuring-suggestions";
        var first = (await client.GetFromJsonAsync<AssistancePage>(path))!;
        first.Items.Should().HaveCount(20); first.NextCursor.Should().NotBeNull();
        var second = (await client.GetFromJsonAsync<AssistancePage>(path + "?cursor=" + Uri.EscapeDataString(first.NextCursor!)))!;
        second.Items.Should().ContainSingle(); second.NextCursor.Should().BeNull();
        first.Items.Select(row => row.Id).Should().NotContain(second.Items.Single().Id);
        (await client.GetAsync(path + "?cursor=invalid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("empty", 400)]
    [InlineData("oversized", 413)]
    [InlineData("media", 415)]
    [InlineData("language", 400)]
    [InlineData("key", 400)]
    public async Task InvalidAudioRequestsNeverCreateResults(string variant, int status)
    {
        using var host = fixture.WithAssistance(true); using var client = host.CreateClient();
        await SignIn(client, DemoDataSeeder.JordanHandle); var draft = await AssistanceApiTests.Create(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{draft.AlertId}/transcriptions")
        { Content = new ByteArrayContent(new byte[variant == "empty" ? 0 : variant == "oversized" ? AssistanceSettings.MaxAudioBytes + 1 : 1]) };
        request.Headers.Add("X-Alert-Draft-Version", "1");
        if (variant != "key") request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        if (variant == "language") request.Headers.Add("X-Audio-Language-Hint", "untrusted-language");
        request.Content.Headers.ContentType = new(variant == "media" ? "text/plain" : "audio/wav");
        using var response = await client.SendAsync(request);
        ((int)response.StatusCode).Should().Be(status);
        await using var db = fixture.CreateContext();
        (await db.AssistanceResults.CountAsync(row => row.AlertId == new AlertId(draft.AlertId))).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedInferenceCannotReplaceManualContentAndMalformedEvidenceIsRejected(bool invalidSpan)
    {
        using var baseHost = fixture.WithAssistance(true);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<IAlertStructuringProvider>(); services.AddSingleton<IAlertStructuringProvider>(new UnsupportedProvider(invalidSpan)); }));
        using var client = host.CreateClient(); await SignIn(client, DemoDataSeeder.JordanHandle);
        var draft = await AssistanceApiTests.Create(client);
        using var generated = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1);
        generated.StatusCode.Should().Be(invalidSpan ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        if (!invalidSpan)
        {
            var result = (await generated.Content.ReadFromJsonAsync<AssistanceResultView>())!;
            using var apply = await AssistanceApiTests.Post(client, draft.AlertId, $"structuring-suggestions/{result.Id}/apply", 1);
            apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await apply.Content.ReadAsStringAsync()).Should().Contain("no-supported-fields");
        }
        (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!.Should().BeEquivalentTo(draft);
    }

    [Theory]
    [InlineData(".5", "mg")]
    [InlineData(",5", "mg")]
    [InlineData("-.5", "mg")]
    [InlineData("+.5", "mg")]
    [InlineData("98", "%")]
    [InlineData("1e-3", "mg")]
    [InlineData("8.2", "mmol/L")]
    public async Task ApplyPreservesExactNumericSpellingAndExplicitUnit(string value, string unit)
    {
        using var host = fixture.WithAssistance(true); using var client = host.CreateClient();
        await SignIn(client, DemoDataSeeder.JordanHandle);
        var draft = await AssistanceApiTests.Create(client);
        using var edit = await client.PatchAsJsonAsync($"/api/v1/alerts/{draft.AlertId}", new UpdateAlertDraftRequest(1,
            draft.Location, draft.UrgencyLabel, "SIMULATION: Situation: fictional value " + value + (unit == "%" ? "" : " ") + unit, draft.Sbar, []));
        edit.EnsureSuccessStatusCode();
        using var generated = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 2);
        var result = (await generated.Content.ReadFromJsonAsync<AssistanceResultView>())!;
        (await AssistanceApiTests.Post(client, draft.AlertId, $"structuring-suggestions/{result.Id}/apply", 2)).EnsureSuccessStatusCode();
        var applied = (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!;
        applied.CriticalFields.Where(field => field.FieldId.StartsWith("assistance-situation-", StringComparison.Ordinal)
            && !field.FieldId.EndsWith("-review", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Match<AlertFieldConfirmationView>(field => field.OriginalValue == value && field.Unit == unit && field.Status == "Unresolved");
    }
    [Fact]
    public async Task ConfirmedAlertCannotGenerateOrApplyAssistance()
    {
        using var host = fixture.WithAssistance(true); using var client = host.CreateClient();
        await SignIn(client, DemoDataSeeder.JordanHandle);
        var confirmation = new AlertConfirmationTests(fixture);
        var prepared = await confirmation.CreateConfirmableAlertAsync(client, sourceText: "SIMULATION: Situation: fictional review source");
        using var generated = await AssistanceApiTests.Post(client, prepared.AlertId, "structuring-suggestions", prepared.Version);
        var result = (await generated.Content.ReadFromJsonAsync<AssistanceResultView>())!;
        (await confirmation.ConfirmAsync(client, prepared.AlertId, prepared.Version, Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();
        using var structure = await AssistanceApiTests.Post(client, prepared.AlertId, "structuring-suggestions", prepared.Version);
        structure.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var apply = await AssistanceApiTests.Post(client, prepared.AlertId, $"structuring-suggestions/{result.Id}/apply", prepared.Version);
        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await apply.Content.ReadAsStringAsync()).Should().Contain("alert-not-editable");
        await using var db = fixture.CreateContext();
        (await db.OutboxMessages.CountAsync(row => row.AggregateId == prepared.AlertId)).Should().Be(1, "only explicit confirmation created dispatch");
    }
    private static async Task SignIn(HttpClient client, string handle) =>
        (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = handle })).EnsureSuccessStatusCode();
    private sealed class UnsupportedProvider(bool invalidSpan) : IAlertStructuringProvider
    {
        public Task<AlertStructuringSuggestion> StructureAsync(AlertStructuringInput input, CancellationToken token) => Task.FromResult(
            new AlertStructuringSuggestion([new("situation", "SIMULATION: invented diagnosis", [new(0, invalidSpan ? int.MaxValue : 10)], null, false)],
                ["background", "assessment", "recommendation"], [], null, "Simulated", "DEMO-1", "DEMO-1"));
    }
}
