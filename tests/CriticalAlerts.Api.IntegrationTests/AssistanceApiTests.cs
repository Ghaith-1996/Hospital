using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AssistanceApiTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task DefaultOffCapabilitiesAndBackendRejectGenerationWhileTypingWorks()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var capabilities = await client.GetFromJsonAsync<JsonObject>("/api/v1/capabilities");
        capabilities!["speechTranscription"]!.GetValue<bool>().Should().BeFalse();
        capabilities["alertStructuringSuggestions"]!.GetValue<bool>().Should().BeFalse();
        var draft = await Create(client);
        using var response = await Post(client, draft.AlertId, "structuring-suggestions", draft.DraftVersion);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("feature-disabled");
    }

    [Fact]
    public async Task StructuringIsImmutableEvidenceUntilHumanApplyAndReplayIsExact()
    {
        using var host = fixture.WithAssistance(true);
        using var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle })).EnsureSuccessStatusCode();
        var draft = await Create(client);
        var key = Guid.NewGuid().ToString("N");
        using var generated = await Post(client, draft.AlertId, "structuring-suggestions", 1, key);
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await generated.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = result["id"]!.GetValue<string>();
        (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!.Should().BeEquivalentTo(draft);
        using var replay = await Post(client, draft.AlertId, "structuring-suggestions", 1, key);
        (await replay.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>().Should().Be(id);
        var applyKey = Guid.NewGuid().ToString("N");
        using var apply = await Post(client, draft.AlertId, $"structuring-suggestions/{id}/apply", 1, applyKey);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!;
        applied.DraftVersion.Should().Be(2);
        applied.SourceText.Should().Be(draft.SourceText);
        applied.UrgencyLabel.Should().Be(draft.UrgencyLabel);
        applied.ApprovedMessage.Should().Be(draft.ApprovedMessage);
        applied.Recipients.Should().BeEquivalentTo(draft.Recipients);
        applied.Sbar!.Situation.Should().Contain("82/54 mmHg");
        applied.CriticalFields.Should().NotBeEmpty().And.OnlyContain(field => field.Status == "Unresolved");
        using var applyReplay = await Post(client, draft.AlertId, $"structuring-suggestions/{id}/apply", 1, applyKey);
        applyReplay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var stale = await Post(client, draft.AlertId, $"structuring-suggestions/{id}/apply", 1);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("suggestion-stale");
        using var history = await client.GetAsync($"/api/v1/alerts/{draft.AlertId}/structuring-suggestions");
        history.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await history.Content.ReadAsStringAsync()).Should().Contain(id);
    }

    [Fact]
    public async Task TranscriptNeverChangesSourceBeforeApplyAndAudioIdentityPreventsConflictingReplay()
    {
        using var host = fixture.WithAssistance(true);
        using var client = host.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle })).EnsureSuccessStatusCode();
        var draft = await Create(client);
        var key = Guid.NewGuid().ToString("N");
        using var generated = await Audio(client, draft.AlertId, key, [1, 2, 3]);
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await generated.Content.ReadFromJsonAsync<JsonObject>())!;
        var id = result["id"]!.GetValue<string>();
        (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!.SourceText.Should().Be(draft.SourceText);
        using var conflict = await Audio(client, draft.AlertId, key, [3, 2, 1]);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var apply = await Post(client, draft.AlertId, $"transcriptions/{id}/apply", 1);
        apply.StatusCode.Should().Be(HttpStatusCode.OK);
        var applied = (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!;
        applied.DraftVersion.Should().Be(2);
        applied.SourceText.Should().Be(result["transcription"]!["transcript"]!.GetValue<string>());
        applied.CriticalFields.Should().OnlyContain(field => field.Status == "Unresolved");
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(DemoDataSeeder.RileyHandle, HttpStatusCode.Forbidden)]
    public async Task GenerationRequiresDraftEditor(string? handle, HttpStatusCode expected)
    {
        using var host = fixture.WithAssistance(true);
        using var client = host.CreateClient();
        if (handle is not null) (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = handle })).EnsureSuccessStatusCode();
        using var response = await Post(client, Guid.NewGuid(), "structuring-suggestions", 1);
        response.StatusCode.Should().Be(expected);
    }

    internal static async Task<AlertDraftView> Create(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("/api/v1/alerts/drafts", new CreateAlertDraftRequest(
            DemoDataSeeder.NorthSiteId.Value, DemoDataSeeder.EmergencyDepartmentId.Value, "SIM-PAT-ASSISTANCE", "Simulation room", "DEMO-URGENT",
            "SIMULATION: Situation: fictional BP 82/54 mmHg\nBackground: no pain\nAssessment: fictional note\nRecommendation: explicit callback request",
            new("SIMULATION: manual situation", "SIMULATION: manual background", "SIMULATION: manual assessment", "SIMULATION: manual recommendation"), []));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<AlertDraftView>())!;
    }
    internal static Task<HttpResponseMessage> Post(HttpClient client, Guid alert, string suffix, int version, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alert}/{suffix}") { Content = JsonContent.Create(new { expectedVersion = version }) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString("N"));
        return client.SendAsync(request);
    }
    internal static Task<HttpResponseMessage> Audio(HttpClient client, Guid alert, string key, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{alert}/transcriptions") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("Idempotency-Key", key);
        request.Headers.Add("X-Alert-Draft-Version", "1");
        request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("audio/webm;codecs=opus");
        return client.SendAsync(request);
    }
}
