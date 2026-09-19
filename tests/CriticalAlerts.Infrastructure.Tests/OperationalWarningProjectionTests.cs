using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Responses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class OperationalWarningProjectionTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task OverdueAndExhaustedRunsProduceGuidanceWhilePauseSuppressesDelay()
    {
        var id = await new EscalationActivationTests(fixture).CreateAlert();
        await using var db = fixture.CreateContext();
        await new EscalationScheduler(db).ScheduleAsync(DemoDataSeeder.OrganizationId, id);
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == id);
        async Task<DateTimeOffset> CurrentMutableTime()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var now = await new DatabaseClock(db).GetUtcNowAsync(timeout.Token);
                if (now >= (run.UpdatedAtUtc ?? run.StartedAtUtc)) return now;
                // Preserve the domain's stale-clock guard when the local VM clock adjusts backwards.
                await Task.Delay(20, timeout.Token);
            }
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 hour' WHERE id = {run.Id.Value}");
        async Task<string[]> Codes()
        {
            await using var read = fixture.CreateContext();
            var live = await new AlertLiveQueryService(read).GetAsync(DemoDataSeeder.OrganizationId, id, true, default);
            live!.OperationalWarnings.Should().OnlyContain(w => !string.IsNullOrWhiteSpace(w.RecommendedApplicationAction));
            return live.OperationalWarnings.Select(w => w.Code).ToArray();
        }
        (await Codes()).Should().Contain("EscalationProcessingDelayed");
        await db.Entry(run).ReloadAsync();
        run.Pause(DemoDataSeeder.JordanUserId, await CurrentMutableTime());
        await db.SaveChangesAsync();
        (await Codes()).Should().NotContain("EscalationProcessingDelayed");
        run.Resume(DemoDataSeeder.JordanUserId, await CurrentMutableTime());
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 hour' WHERE id = {run.Id.Value}");
        var claim = await new EscalationRunRepository(db).TryClaimAsync(run.Id, "warning-test", TimeSpan.FromSeconds(30));
        claim.Should().NotBeNull();
        (await new EscalationRunProcessor(db).ProcessClaimAsync(claim!, (_, _) => Task.CompletedTask)).Should().BeTrue();
        (await Codes()).Should().Contain("EscalationExhausted").And.NotContain("EscalationProcessingDelayed");
    }

    [Theory]
    [InlineData(DirectorySyncRunStatus.Failed)]
    [InlineData(DirectorySyncRunStatus.Partial)]
    public async Task LatestFailedSynchronizationProducesFixedGuidanceWithoutErrorSummary(DirectorySyncRunStatus status)
    {
        var id = await new EscalationActivationTests(fixture).CreateAlert();
        await using var db = fixture.CreateContext();
        var now = await new DatabaseClock(db).GetUtcNowAsync();
        var sync = DirectorySyncRun.CreateCompleted(DirectorySyncRunId.New(), DemoDataSeeder.OrganizationId,
            "SIM-DIRECTORY", now, now, 0, 0, 0, 1, status, Guid.NewGuid().ToString("D"), "SIM-SECRET-PHASE10-SENTINEL");
        db.DirectorySyncRuns.Add(sync);
        await db.SaveChangesAsync();
        try
        {
            var live = await new AlertLiveQueryService(db).GetAsync(DemoDataSeeder.OrganizationId, id, true, default);
            var warning = live!.OperationalWarnings.Single(w => w.Code == "DirectorySynchronizationFailed");
            warning.RequiresHospitalFallback.Should().BeTrue();
            warning.RecommendedApplicationAction.Should().Contain("REQUIRES_HOSPITAL_DECISION");
            System.Text.Json.JsonSerializer.Serialize(live).Contains("SIM-SECRET-PHASE10-SENTINEL", StringComparison.Ordinal).Should().BeFalse();
        }
        finally { db.DirectorySyncRuns.Remove(sync); await db.SaveChangesAsync(); }
    }
}
