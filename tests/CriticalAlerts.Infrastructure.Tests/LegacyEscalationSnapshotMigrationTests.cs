using System.Security.Cryptography;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class LegacyEscalationSnapshotMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task LegacyActiveConfirmedAlertUpgradesWithoutFabricatedSnapshotOrEligibility()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = AesGcmSensitiveDataProtector.FromBase64(key);
        var reference = protector.Protect("SIM-PAT-LEGACY-ESCALATION", new SensitiveDataContext(
            ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value));
        var id = AlertId.New();
        var now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");
        await using (var legacy = DatabaseOperations.CreateContext(fixture.ConnectionString))
        {
            await legacy.GetService<IMigrator>().MigrateAsync("20260903014910_ComplianceDataProtection");
            await new DemoDataSeeder(legacy, key).SeedAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO alerts (id, organization_id, site_id, department_id, created_by_user_id,
                    simulation_patient_reference_legacy, simulation_patient_reference_ciphertext, simulation_patient_reference_key_version, simulation_patient_reference_purpose,
                    location, urgency_label, source_type, state, draft_version, confirmed_draft_version,
                    confirmed_by_user_id, confirmed_at_utc, demo_escalation_policy_version, demo_notification_policy_version,
                    created_at_utc, updated_at_utc)
                VALUES ({id.Value}, {DemoDataSeeder.OrganizationId.Value}, {DemoDataSeeder.NorthSiteId.Value},
                    {DemoDataSeeder.EmergencyDepartmentId.Value}, {DemoDataSeeder.JordanUserId.Value},
                    'SIM-PAT-LEGACY-ESCALATION', {reference.Ciphertext}, {reference.KeyVersion}, {reference.Purpose},
                    'SIMULATION legacy room', 'DEMO-URGENT', 'Typed', 'Active', 3, 3,
                    {DemoDataSeeder.JordanUserId.Value}, {now}, 'DEMO', 'DEMO', {now}, {now})
                """);
        }
        await using (var legacy = DatabaseOperations.CreateContext(fixture.ConnectionString))
        {
            for (var index = 0; index < 2; index++)
                await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO escalation_runs (id, organization_id, alert_id, policy_id, policy_version, current_step, next_due_at_utc, state, started_at_utc)
                    VALUES ({Guid.NewGuid()}, {DemoDataSeeder.OrganizationId.Value}, {id.Value}, {Guid.NewGuid()}, 'DEMO', 1, {now}, 'Scheduled', {now})
                    """);
        }
        await DatabaseOperations.MigrateAsync(fixture.ConnectionString, key);
        await using var verify = DatabaseOperations.CreateContext(fixture.ConnectionString);
        var alert = await verify.Alerts.SingleAsync(a => a.Id == id);
        alert.State.Should().Be(AlertState.Active);
        alert.ConfirmedDraftVersion!.Value.Value.Should().Be(3);
        alert.AutomaticEscalationEligible.Should().BeFalse();
        alert.ExactEscalationPlanId.Should().BeNull();
        alert.ExactEscalationPolicyId.Should().BeNull();
        alert.ExactEscalationPolicyVersion.Should().BeNull();
        alert.ExactEscalationPlanRevision.Should().BeNull();
        (await verify.AlertEscalationPlans.CountAsync()).Should().Be(0);
        (await verify.AlertEscalationRecipientSnapshots.CountAsync()).Should().Be(0);
        var legacyRuns = await verify.EscalationRuns.Where(r => r.AlertId == id).ToArrayAsync();
        legacyRuns.Should().HaveCount(2);
        foreach (var run in legacyRuns)
        {
            run.AlertVersion.Should().BeNull();
            run.PlanId.Should().BeNull();
            run.PlanRevision.Should().BeNull();
            run.UpdatedAtUtc.Should().BeNull();
            var claim = () => run.AcquireLease("demo-worker", now, TimeSpan.FromMinutes(1));
            claim.Should().Throw<DomainException>();
        }
        protector.Unprotect(alert.SimulationPatientReference, new SensitiveDataContext(
            ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value)).Should().Be("SIM-PAT-LEGACY-ESCALATION");
    }
}
