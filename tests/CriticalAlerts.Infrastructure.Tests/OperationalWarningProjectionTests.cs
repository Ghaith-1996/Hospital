using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
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
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db, 60);
        await new EscalationProcessor(db).ProcessNextAsync("warning-test");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET next_due_at_utc = clock_timestamp() - interval '1 hour', next_check_at_utc = clock_timestamp() - interval '1 second' WHERE alert_id = {approval.AlertId.Value}");
        db.ChangeTracker.Clear();
        var run = await db.EscalationRuns.SingleAsync(x => x.AlertId == approval.AlertId);
        async Task<string[]> Codes()
        {
            await using var read = fixture.CreateContext();
            var live = await new AlertLiveQueryService(read, TimeProvider.System).GetAsync(DemoDataSeeder.OrganizationId, approval.AlertId, default);
            live!.OperationalWarnings.Should().OnlyContain(w => !string.IsNullOrWhiteSpace(w.RecommendedApplicationAction));
            return live.OperationalWarnings.Select(w => w.Code).ToArray();
        }
        (await Codes()).Should().Contain("EscalationProcessingDelayed");
        run.Pause(TimeProvider.System.GetUtcNow());
        await db.SaveChangesAsync();
        (await Codes()).Should().NotContain("EscalationProcessingDelayed");
        run.Resume(TimeProvider.System.GetUtcNow());
        await db.SaveChangesAsync();
        run.Advance(null, run.HandledNegativeResponses, TimeProvider.System.GetUtcNow());
        await db.SaveChangesAsync();
        (await Codes()).Should().Contain("EscalationExhausted").And.NotContain("EscalationProcessingDelayed");
    }

    [Theory]
    [InlineData(DirectorySyncRunStatus.Failed)]
    [InlineData(DirectorySyncRunStatus.Partial)]
    public async Task LatestFailedSynchronizationProducesFixedGuidanceWithoutErrorSummary(DirectorySyncRunStatus status)
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db);
        var now = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        var sync = DirectorySyncRun.CreateCompleted(DirectorySyncRunId.New(), DemoDataSeeder.OrganizationId,
            "SIM-DIRECTORY", now, now, 0, 0, 0, 1, status, Guid.NewGuid().ToString("D"), "SIM-SECRET-PHASE10-SENTINEL");
        db.DirectorySyncRuns.Add(sync);
        await db.SaveChangesAsync();
        try
        {
            var live = await new AlertLiveQueryService(db, TimeProvider.System).GetAsync(DemoDataSeeder.OrganizationId, approval.AlertId, default);
            var warning = live!.OperationalWarnings.Single(w => w.Code == "DirectorySynchronizationFailed");
            warning.RequiresHospitalFallback.Should().BeTrue();
            warning.RecommendedApplicationAction.Should().Contain("REQUIRES_HOSPITAL_DECISION");
            System.Text.Json.JsonSerializer.Serialize(live).Contains("SIM-SECRET-PHASE10-SENTINEL", StringComparison.Ordinal).Should().BeFalse();
        }
        finally { db.DirectorySyncRuns.Remove(sync); await db.SaveChangesAsync(); }
    }
}
