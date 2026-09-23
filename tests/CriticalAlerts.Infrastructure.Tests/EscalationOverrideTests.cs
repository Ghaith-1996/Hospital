using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Responses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationOverrideTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task LiveReadsExposeSafeEscalationAndProvenanceWithoutAdvancingTheRun()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db, 0);
        await new EscalationProcessor(db).ProcessNextAsync("worker");
        var count = await db.EscalationEvents.CountAsync();
        var query = new AlertLiveQueryService(db, TimeProvider.System);
        var view = await query.GetAsync(approval.OrganizationId, approval.AlertId, default);
        view!.Escalation.Should().NotBeNull();
        view.Escalation!.State.Should().Be("Exhausted");
        view.ManualFallbackRequired.Should().BeTrue();
        view.Recipients.Single(row => row.PractitionerId == DemoDataSeeder.RileySatoId.Value).SelectionSources.Should().Contain("EscalationPolicy");
        var json = System.Text.Json.JsonSerializer.Serialize(view.Escalation);
        json.Should().NotContain("SIM-PAT").And.NotContain("Ciphertext").And.NotContain("Endpoint").And.NotContain("Riley");
        await query.GetAsync(approval.OrganizationId, approval.AlertId, default);
        (await db.EscalationEvents.CountAsync()).Should().Be(count);
        (await query.GetAsync(OrganizationId.New(), approval.AlertId, default)).Should().BeNull();
    }

    [Fact]
    public async Task PauseAndResumePersistRemainingDelayAndReplayWithoutDuplicateEvents()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db);
        await new EscalationProcessor(db).ProcessNextAsync("worker");
        var service = new AlertLifecycleService(db, TimeProvider.System);
        var request = new EscalationOverrideRequest(approval.AlertVersion.Value, "simulation-pause-requested");
        (await service.SetEscalationPausedAsync(approval.OrganizationId, DemoDataSeeder.JordanUserId, "test", approval.AlertId,
            request, "pause-once", true, default))!.State.Should().Be("Paused");
        db.ChangeTracker.Clear();
        var remaining = (await db.EscalationRuns.SingleAsync()).RemainingDelay;
        remaining.Should().NotBeNull();
        remaining!.Value.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
        (await service.SetEscalationPausedAsync(approval.OrganizationId, DemoDataSeeder.JordanUserId, "test", approval.AlertId,
            request, "pause-once", true, default))!.Replayed.Should().BeTrue();
        await using var restarted = fixture.CreateContext();
        await new AlertLifecycleService(restarted, TimeProvider.System).SetEscalationPausedAsync(approval.OrganizationId,
            DemoDataSeeder.JordanUserId, "test", approval.AlertId,
            new(approval.AlertVersion.Value, "simulation-resume-requested"), "resume-once", false, default);
        db.ChangeTracker.Clear();
        var run = await db.EscalationRuns.SingleAsync();
        run.State.Should().Be(EscalationRunState.Scheduled);
        (await db.EscalationEvents.CountAsync(row => row.Kind == EscalationEventKind.Paused)).Should().Be(1);
        (await db.EscalationEvents.CountAsync(row => row.Kind == EscalationEventKind.Resumed)).Should().Be(1);
        (await db.AuditEvents.CountAsync(row => row.Action.StartsWith("escalation."))).Should().Be(2);
    }

    [Theory]
    [InlineData(0, "simulation-pause-requested", "stale")]
    [InlineData(1, "free text forbidden", "invalid-reason")]
    [InlineData(1, "simulation-pause-requested", "")]
    public async Task InvalidOverridesMakeNoChanges(int versionOffset, string reason, string key)
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db);
        await new EscalationProcessor(db).ProcessNextAsync("worker");
        var version = versionOffset == 0 ? approval.AlertVersion.Value - 1 : approval.AlertVersion.Value;
        var action = () => new AlertLifecycleService(db, TimeProvider.System).SetEscalationPausedAsync(approval.OrganizationId,
            DemoDataSeeder.JordanUserId, "test", approval.AlertId, new(version, reason), key, true, default);
        await action.Should().ThrowAsync<AlertLifecycleValidationException>();
        db.ChangeTracker.Clear();
        (await db.EscalationRuns.SingleAsync()).State.Should().Be(EscalationRunState.Scheduled);
    }
}
