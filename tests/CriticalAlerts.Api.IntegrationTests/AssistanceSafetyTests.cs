using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Assistance;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AssistanceSafetyTests(SeededPostgresApiFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentGenerationCallsProviderOnceAndCompletionAfterEditStaysStale(bool speech)
    {
        var gate = new GatedProvider();
        using var baseHost = fixture.WithAssistance(true);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAlertStructuringProvider>(); services.AddSingleton<IAlertStructuringProvider>(gate);
            services.RemoveAll<ITranscriptionProvider>(); services.AddSingleton<ITranscriptionProvider>(gate);
        }));
        using var client = host.CreateClient();
        await SignIn(client);
        var draft = await AssistanceApiTests.Create(client);
        var key = Guid.NewGuid().ToString("N");
        Task<HttpResponseMessage> Generate() => speech ? AssistanceApiTests.Audio(client, draft.AlertId, key, [1, 2, 3])
            : AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1, key);
        var pending = Generate();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var duplicate = await Generate();
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("operation-in-progress");
        using var edit = await client.PatchAsJsonAsync($"/api/v1/alerts/{draft.AlertId}", new UpdateAlertDraftRequest(1,
            draft.Location, draft.UrgencyLabel, "SIMULATION: revised human source", draft.Sbar, []));
        edit.EnsureSuccessStatusCode();
        gate.Release.TrySetResult();
        using var response = await pending;
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        result["stale"]!.GetValue<bool>().Should().BeTrue();
        gate.Calls.Should().Be(1);
        using var replay = await Generate();
        replay.EnsureSuccessStatusCode();
        gate.Calls.Should().Be(1);
        var suffix = speech ? "transcriptions" : "structuring-suggestions";
        using var apply = await AssistanceApiTests.Post(client, draft.AlertId, $"{suffix}/{result["id"]!.GetValue<string>()}/apply", 2);
        apply.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!;
        current.SourceText.Should().Be("SIMULATION: revised human source");
        current.DraftVersion.Should().Be(2);
        await using var db = fixture.CreateContext();
        (await db.AssistanceResults.CountAsync(row => row.AlertId == new AlertId(draft.AlertId))).Should().Be(1);
        (await db.Alerts.SingleAsync(row => row.Id == new AlertId(draft.AlertId))).ConfirmedDraftVersion.Should().BeNull();
        (await db.AlertRecipientSelections.CountAsync(row => row.AlertId == new AlertId(draft.AlertId))).Should().Be(0);
        (await db.ResponsibilityAssignments.CountAsync(row => row.AlertId == new AlertId(draft.AlertId))).Should().Be(0);
        (await db.EscalationRuns.CountAsync(row => row.AlertId == new AlertId(draft.AlertId))).Should().Be(0);
        (await db.OutboxMessages.CountAsync(row => row.AggregateId == draft.AlertId)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentApplyMutatesExactlyOnce(bool sameKey)
    {
        using var host = fixture.WithAssistance(true);
        using var client = host.CreateClient(); await SignIn(client);
        var draft = await AssistanceApiTests.Create(client);
        using var generation = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 1);
        var result = (await generation.Content.ReadFromJsonAsync<JsonObject>())!;
        var suffix = $"structuring-suggestions/{result["id"]!.GetValue<string>()}/apply";
        var key = Guid.NewGuid().ToString("N");
        var responses = await Task.WhenAll(AssistanceApiTests.Post(client, draft.AlertId, suffix, 1, key),
            AssistanceApiTests.Post(client, draft.AlertId, suffix, 1, sameKey ? key : null));
        responses.Count(response => response.IsSuccessStatusCode).Should().Be(sameKey ? 2 : 1);
        (await client.GetFromJsonAsync<AlertDraftView>($"/api/v1/alerts/{draft.AlertId}"))!.DraftVersion.Should().Be(2);
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task RuntimeLogsMetricsAuditErrorsAndHealthExcludeAllPayloads()
    {
        const string sentinel = "SIM-PHASE11-TRANSCRIPT-SECRET";
        var logs = new CapturingLoggerProvider();
        using var baseHost = fixture.WithAssistance(true);
        using var host = baseHost.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        { services.RemoveAll<ITranscriptionProvider>(); services.AddSingleton<ITranscriptionProvider>(new SentinelProvider(sentinel)); }));
        host.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        using var scope = host.Services.CreateScope();
        var meter = scope.ServiceProvider.GetRequiredService<PlatformMetrics>().Meter;
        var observations = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, observer) => { if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => observations.Add(instrument.Name + JsonSerializer.Serialize(tags.ToArray())));
        listener.Start();
        using var client = host.CreateClient(); await SignIn(client);
        var draft = await AssistanceApiTests.Create(client);
        using var generated = await AssistanceApiTests.Audio(client, draft.AlertId, Guid.NewGuid().ToString("N"), Encoding.UTF8.GetBytes("SIM-PHASE11-AUDIO-SENTINEL"));
        generated.EnsureSuccessStatusCode();
        var result = (await generated.Content.ReadFromJsonAsync<JsonObject>())!;
        using var applied = await AssistanceApiTests.Post(client, draft.AlertId, $"transcriptions/{result["id"]!.GetValue<string>()}/apply", 1);
        applied.EnsureSuccessStatusCode();
        using var stale = await AssistanceApiTests.Post(client, draft.AlertId, $"transcriptions/{result["id"]!.GetValue<string>()}/apply", 1);
        using var structure = await AssistanceApiTests.Post(client, draft.AlertId, "structuring-suggestions", 2);
        structure.EnsureSuccessStatusCode();
        var structured = (await structure.Content.ReadFromJsonAsync<JsonObject>())!;
        using var appliedStructure = await AssistanceApiTests.Post(client, draft.AlertId, $"structuring-suggestions/{structured["id"]!.GetValue<string>()}/apply", 2);
        appliedStructure.EnsureSuccessStatusCode();
        using var failedRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/alerts/{draft.AlertId}/transcriptions") { Content = new ByteArrayContent([1]) };
        failedRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N")); failedRequest.Headers.Add("X-Alert-Draft-Version", "3");
        failedRequest.Headers.Add("X-Simulation-Scenario", "provider-outage"); failedRequest.Content.Headers.ContentType = new("audio/wav");
        using var failed = await client.SendAsync(failedRequest);
        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        await using var db = fixture.CreateContext();
        var confirmations = await db.AlertFieldConfirmations.Where(row => row.AlertId == new AlertId(draft.AlertId)).ToArrayAsync();
        JsonSerializer.Serialize(confirmations).Should().NotContain(sentinel);
        var audits = await db.AuditEvents.Where(row => row.ResourceId == draft.AlertId).ToArrayAsync();
        var projection = JsonSerializer.Serialize(audits.Select(AuditSafety.Project));
        projection.Should().Contain("transcription.completed").And.Contain("transcription.failed").And.Contain("structuring.applied");
        var safe = string.Join("\n", logs.Entries.Concat(observations)) + projection + await failed.Content.ReadAsStringAsync()
            + await stale.Content.ReadAsStringAsync() + await client.GetStringAsync("/health/live");
        safe.Should().NotContain(sentinel).And.NotContain("SIM-PHASE11-AUDIO-SENTINEL").And.NotContain("SIM-PHASE11-SOURCE-SECRET");
        observations.Should().Contain(value => value.StartsWith("criticalalerts.transcription.requests", StringComparison.Ordinal));
    }
    private static async Task SignIn(HttpClient client) => (await client.PostAsJsonAsync("/api/v1/dev/session", new { simulationHandle = DemoDataSeeder.JordanHandle })).EnsureSuccessStatusCode();
    private sealed class GatedProvider : IAlertStructuringProvider, ITranscriptionProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<AlertStructuringSuggestion> StructureAsync(AlertStructuringInput input, CancellationToken token)
        { Interlocked.Increment(ref Calls); Entered.TrySetResult(); await Release.Task.WaitAsync(token); return await new SimulatedAlertStructuringProvider().StructureAsync(input, token); }
        public async Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken token)
        { Interlocked.Increment(ref Calls); Entered.TrySetResult(); await Release.Task.WaitAsync(token); return await new SimulatedTranscriptionProvider().TranscribeAsync(input, options, token); }
    }
    private sealed class SentinelProvider(string sentinel) : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken token)
            => options.SimulationScenario is not null ? throw new InvalidOperationException(sentinel)
                : Task.FromResult(new TranscriptionResult("SIMULATION: Situation: " + sentinel, [], null, null, "Simulated", "DEMO-1"));
    }
}

