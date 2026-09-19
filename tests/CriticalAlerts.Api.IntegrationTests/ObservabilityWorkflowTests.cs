using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

// Own host/database: observing actual operations must not depend on other tests' queued work.
public sealed class ObservabilityWorkflowTests : IAsyncLifetime
{
    private readonly SeededPostgresApiFixture fixture = new();
    private static readonly string[] Sentinels = ["SIM-PATIENT-PHASE10-SENTINEL", "SIM-SECRET-PHASE10-SENTINEL",
        "+1-555-PHASE10", "phase10@example.invalid", "SIM-APPROVED-MESSAGE-DO-NOT-LOG"];
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task RealWorkflowLogsMetricsProblemsHealthAndAuditExcludeProtectedSentinels()
    {
        var tags = new List<KeyValuePair<string, object?>>();
        var measurements = new List<string>();
        using var metricScope = fixture.CreateServiceScope();
        var meter = metricScope.ServiceProvider.GetRequiredService<PlatformMetrics>().Meter;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, observer) =>
        { if (ReferenceEquals(instrument.Meter, meter)) observer.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, values, _) =>
        { measurements.Add(instrument.Name); tags.AddRange(values.ToArray()); });
        listener.Start();
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        operatorClient.DefaultRequestHeaders.Add("X-Correlation-ID", Sentinels[0]);
        fixture.ClearLogs();
        var safeOutputs = new List<string>();
        using (var invalidDraft = await operatorClient.PostAsJsonAsync("/api/v1/alerts/drafts",
            new { simulationPatientReference = Sentinels[0], sourceText = Sentinels[1] }))
        {
            invalidDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            safeOutputs.Add(await invalidDraft.Content.ReadAsStringAsync());
        }

        using var administrator = await fixture.CreateSignedInClientAsync(DemoDataSeeder.MorganHandle);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "fixtures/simulation/directory-harborview.csv"))) directory = directory.Parent;
        directory.Should().NotBeNull();
        var csv = File.ReadAllText(Path.Combine(directory!.FullName, "fixtures/simulation/directory-harborview.csv"));
        using (var rejectedInput = Csv(csv.Replace("Maya", Sentinels[0], StringComparison.Ordinal)))
        using (var rejectedPreview = await administrator.PostAsync("/api/v1/directory/imports/preview", rejectedInput))
        {
            var rejected = (await rejectedPreview.Content.ReadFromJsonAsync<DirectoryImportPreviewResult>())!;
            rejected.Errors.Count.Should().BeGreaterThan(0);
            safeOutputs.Add(await rejectedPreview.Content.ReadAsStringAsync());
        }
        using (var previewInput = Csv(csv))
        using (var previewResponse = await administrator.PostAsync("/api/v1/directory/imports/preview", previewInput))
        {
            previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var preview = (await previewResponse.Content.ReadFromJsonAsync<DirectoryImportPreviewResult>())!;
            preview.Errors.Should().BeEmpty();
            using var applyInput = Csv(csv, preview.PreviewToken);
            using var applied = await administrator.PostAsync("/api/v1/directory/imports", applyInput);
            applied.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var workflow = new AlertConfirmationTests(fixture);
        var alert = await workflow.CreateConfirmableAlertAsync(operatorClient, Sentinels[0], "SIMULATION: " + string.Join(" ", Sentinels),
            "SIMULATION: " + string.Join(" ", Sentinels), "Riley");
        using (var confirmed = await workflow.ConfirmAsync(operatorClient, alert.AlertId, alert.Version, Guid.NewGuid().ToString("N")))
        {
            confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
            var correlation = confirmed.Headers.GetValues("X-Correlation-ID").Single();
            Guid.TryParse(correlation, out _).Should().BeTrue();
            await using var read = fixture.CreateContext();
            (await read.AuditEvents.SingleAsync(e => e.Action == "alert.confirmed" && e.ResourceId == alert.AlertId)).CorrelationId.Should().Be(correlation);
        }
        using (var scope = fixture.CreateServiceScope())
            (await scope.ServiceProvider.GetRequiredService<IOutboxDispatchProcessor>().ProcessNextAsync("phase10", default)).Processed.Should().BeTrue();

        // Exercise Phase 9 validation failure using the actual scheduler/repository/processor.
        using (var scope = fixture.CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CriticalAlertsDbContext>();
            await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, new AlertId(alert.AlertId));
            var run = await db.EscalationRuns.SingleAsync(r => r.AlertId == new AlertId(alert.AlertId));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 second' WHERE id = {run.Id.Value}");
            var snapshot = await db.AlertEscalationRecipientSnapshots.FirstAsync(s => s.AlertId == new AlertId(alert.AlertId));
            await db.Practitioners.Where(p => p.Id == snapshot.PractitionerId).ExecuteUpdateAsync(p => p.SetProperty(v => v.IsActive, false));
            var claim = await new EscalationRunRepository(db).TryClaimAsync(run.Id, "phase10", TimeSpan.FromSeconds(30));
            claim.Should().NotBeNull();
            (await new EscalationRunProcessor(db).ProcessClaimAsync(claim!)).Should().BeTrue();
            await db.Practitioners.Where(p => p.Id == snapshot.PractitionerId).ExecuteUpdateAsync(p => p.SetProperty(v => v.IsActive, true));
        }
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        await Command(practitioner, $"/api/v1/my-alerts/{alert.AlertId}/responses", new { expectedVersion = alert.Version, responseType = "Accepted" });
        await Command(operatorClient, $"/api/v1/alerts/{alert.AlertId}/resolve", new { expectedVersion = alert.Version });
        using (var invalidResponse = await practitioner.PostAsJsonAsync($"/api/v1/my-alerts/{alert.AlertId}/responses",
            new { expectedVersion = alert.Version, responseType = Sentinels[0], reasonCode = Sentinels[1] }))
            safeOutputs.Add(await invalidResponse.Content.ReadAsStringAsync());

        // The failing adapter throws a protected sentinel; production exception handling must discard it.
        var failed = await workflow.CreateConfirmableAlertAsync(operatorClient, Sentinels[0], "SIMULATION: " + string.Join(" ", Sentinels), "SIMULATION: " + Sentinels[4], "Riley");
        using (var confirmation = await workflow.ConfirmAsync(operatorClient, failed.AlertId, failed.Version, Guid.NewGuid().ToString("N")))
            confirmation.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = fixture.CreateServiceScope())
        {
            var services = scope.ServiceProvider;
            var processor = new OutboxDispatchProcessor(services.GetRequiredService<CriticalAlertsDbContext>(),
                [new FailingChannel()], services.GetRequiredService<INotificationStatusNormalizer>(),
                services.GetRequiredService<ISimulationDispatchScenarioStore>(), TimeProvider.System,
                Options.Create(new DispatchWorkerOptions { MaxAttempts = 1 }), services.GetRequiredService<ILogger<OutboxDispatchProcessor>>(),
                services.GetRequiredService<ILoggerFactory>());
            (await processor.ProcessNextAsync("phase10", default)).PermanentlyFailed.Should().BeTrue();
        }
        using var auditor = await fixture.CreateSignedInClientAsync(DemoDataSeeder.AveryHandle);
        using (var audit = await auditor.GetAsync("/api/v1/admin/audit"))
        { audit.StatusCode.Should().Be(HttpStatusCode.OK); safeOutputs.Add(await audit.Content.ReadAsStringAsync()); }
        using (var health = await operatorClient.GetAsync("/health/ready"))
        { health.StatusCode.Should().Be(HttpStatusCode.OK); safeOutputs.Add(await health.Content.ReadAsStringAsync()); }
        var logs = string.Join("\n", fixture.LogEntries);
        foreach (var action in new[] { "alert.confirmed", "directory.import.applied", "recipient.response.accepted", "alert.resolved",
            "dispatch.failed", "audit.read", "escalation-processing-failed" }) logs.Should().Contain(action);
        measurements.Should().Contain("criticalalerts.alert.confirmations").And.Contain("criticalalerts.audit.queries").And.Contain("criticalalerts.escalation.failures");
        tags.Should().OnlyContain(tag => tag.Key == "operation");
        var all = string.Join("\n", safeOutputs) + logs + string.Join(" ", tags.Select(tag => tag.Value));
        foreach (var sentinel in Sentinels) all.Contains(sentinel, StringComparison.Ordinal).Should().BeFalse();
    }

    private static MultipartFormDataContent Csv(string csv, string? token = null)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "simulation.csv");
        if (token is not null) content.Add(new StringContent(token), "preview_token");
        return content;
    }

    private static async Task Command(HttpClient client, string path, object value)
    {
        var key = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(value) };
            request.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Conflict && attempt < 29) { await Task.Delay(100); continue; }
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return;
        }
    }

    private sealed class FailingChannel : INotificationChannel
    {
        public NotificationChannel ChannelType => NotificationChannel.SecureMessage;
        public string ProviderName => "simulation-secure-message";
        public Task<NotificationDispatchResult> DispatchAsync(NotificationDispatchRequest request, SimulationDispatchScenario scenario, CancellationToken cancellationToken)
            => throw new InvalidOperationException(string.Join(" ", Sentinels));
    }
}
