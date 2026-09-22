using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationPersistenceTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task TimelineIsAppendOnlyAndCannotCrossOrganizations()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await SeedApprovalAsync(db);
        var now = DateTimeOffset.UtcNow;
        var run = EscalationRun.Schedule(EscalationRunId.New(), approval.OrganizationId, approval.AlertId,
            approval.PolicyId, approval.PolicyVersion, now.AddMinutes(1), now, approval.AlertVersion);
        db.EscalationRuns.Add(run);
        db.EscalationEvents.Add(run.Record(EscalationEventKind.Scheduled, now));
        await db.SaveChangesAsync();
        var erase = () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM escalation_events WHERE run_id = {run.Id.Value}");
        await erase.Should().ThrowAsync<PostgresException>();
        var foreign = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO escalation_events (id, run_id, organization_id, sequence, kind, step, occurred_at_utc)
            VALUES ({Guid.NewGuid()}, {run.Id.Value}, {Guid.NewGuid()}, 2, 'Paused', 1, {now})
            """);
        await foreign.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task ExactVersionRunIsUniqueAndPauseAndLeaseSurviveReload()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await SeedApprovalAsync(db);
        var now = DateTimeOffset.UtcNow;
        var run = EscalationRun.Schedule(EscalationRunId.New(), approval.OrganizationId, approval.AlertId,
            approval.PolicyId, approval.PolicyVersion, now.AddMinutes(1), now, approval.AlertVersion);
        run.Pause(now.AddSeconds(20));
        run.TryAcquireLease("worker-before-restart", now, TimeSpan.FromMinutes(1));
        db.EscalationRuns.Add(run);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var restored = await db.EscalationRuns.SingleAsync(row => row.Id == run.Id);
        restored.AlertVersion.Should().Be(approval.AlertVersion);
        restored.RemainingDelay.Should().Be(TimeSpan.FromSeconds(40));
        restored.LeaseOwner.Should().Be("worker-before-restart");
        db.EscalationRuns.Add(EscalationRun.Schedule(EscalationRunId.New(), approval.OrganizationId, approval.AlertId,
            approval.PolicyId, approval.PolicyVersion, now.AddMinutes(1), now, approval.AlertVersion));
        var duplicate = () => db.SaveChangesAsync();
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PublishedPolicyAndStepCannotBeRewrittenUnderTheSameVersion()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var policy = await db.EscalationPolicies.SingleAsync(row => row.IsActive);
        var edit = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE escalation_policies SET stop_condition = 'changed' WHERE id = {policy.Id.Value}");
        var stepEdit = () => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE escalation_steps SET delay = interval '0 seconds' WHERE policy_id = {policy.Id.Value}");
        await edit.Should().ThrowAsync<PostgresException>();
        await stepEdit.Should().ThrowAsync<PostgresException>();
    }

    internal static async Task<ConfirmedEscalationPlan> SeedApprovalAsync(CriticalAlertsDbContext db, long delaySeconds = 60)
    {
        var alertId = await OutboxDispatchProcessorTests.SeedConfirmedAlertAsync(db, NotificationChannel.SecureMessage);
        var alert = await db.Alerts.SingleAsync(row => row.Id == alertId);
        var policy = await db.EscalationPolicies.SingleAsync(row => row.IsActive);
        var step = await db.EscalationSteps.SingleAsync(row => row.PolicyId == policy.Id);
        var plan = new EscalationPlanView(policy.Id.Value, policy.Version, "SIM-APPROVED-PLAN",
            [new EscalationPlanStepView(step.Id.Value, 1, delaySeconds,
                [new EscalationPlanRecipient(DemoDataSeeder.RileySatoId.Value, null, "Riley Sato", "Neurology",
                    "Fictional Emergency", "Fictional North", null, "SecureMessage", "SIM-BACKUP-REVISION", null, "Backup")])]);
        var approval = ConfirmedEscalationPlan.Capture(alert.OrganizationId, alert.Id, alert.DraftVersion,
            policy.Id, policy.Version, plan.Revision, JsonSerializer.Serialize(plan), DemoDataSeeder.JordanUserId, DateTimeOffset.UtcNow);
        db.ConfirmedEscalationPlans.Add(approval);
        alert.MarkActive(DateTimeOffset.UtcNow, "simulation-phase9-test");
        await db.SaveChangesAsync();
        return approval;
    }
}
