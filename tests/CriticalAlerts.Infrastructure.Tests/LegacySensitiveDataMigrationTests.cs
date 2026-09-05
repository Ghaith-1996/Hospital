using System.Security.Cryptography;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

// A separate container keeps the deliberately old schema away from current-schema fixtures.
public sealed class LegacySensitiveDataMigrationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture fixture;
    private static readonly DateTimeOffset CreatedAt = DateTimeOffset.Parse("2026-08-30T12:00:00Z");

    public LegacySensitiveDataMigrationTests(PostgresFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPlaintextAlertUpgradesWithoutLosingPatientOrOriginalSource(bool retryAfterMissingKey)
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = AesGcmSensitiveDataProtector.FromBase64(key);
        var organizationId = OrganizationId.New();
        var siteId = SiteId.New();
        var departmentId = DepartmentId.New();
        var userId = UserId.New();
        var alertId = AlertId.New();
        var sourceContext = new SensitiveDataContext(ProtectedValuePurposes.AlertTypedSource, organizationId.Value);
        var source = protector.Protect("SIMULATION: original legacy source", sourceContext);
        await using (var legacy = DatabaseOperations.CreateContext(fixture.ConnectionString))
        {
            await legacy.Database.EnsureDeletedAsync();
            await legacy.GetService<IMigrator>().MigrateAsync("20260830160849_Phase8PractitionerResponses");
            legacy.Organizations.Add(Organization.CreateSimulation(organizationId, "Fictional Migration Hospital", CreatedAt));
            legacy.Sites.Add(Site.Create(siteId, organizationId, "Fictional Migration Site", "SIM-SITE-MIGRATION", CreatedAt));
            legacy.Departments.Add(Department.Create(departmentId, organizationId, siteId,
                "Fictional Migration Department", "SIM-DEPT-MIGRATION", CreatedAt));
            legacy.Users.Add(UserAccount.CreateSimulation(userId, organizationId, "Fictional Migration Operator",
                "sim-migration-operator", CreatedAt));
            await legacy.SaveChangesAsync();
            await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO alerts (
                    id, organization_id, site_id, department_id, created_by_user_id,
                    simulation_patient_reference, location, urgency_label, source_type,
                    original_source_ciphertext, original_source_key_version, original_source_purpose,
                    state, draft_version, demo_escalation_policy_version, demo_notification_policy_version,
                    created_at_utc, updated_at_utc)
                VALUES ({alertId.Value}, {organizationId.Value}, {siteId.Value}, {departmentId.Value}, {userId.Value},
                    'SIM-PAT-LEGACY', 'SIMULATION migration room', 'DEMO-URGENT', 'Typed',
                    {source.Ciphertext}, {source.KeyVersion}, {source.Purpose},
                    'Draft', 3, 'DEMO', 'DEMO', {CreatedAt}, {CreatedAt});
                """);
        }

        if (retryAfterMissingKey)
        {
            var migrateWithoutKey = async () => await DatabaseOperations.MigrateAsync(fixture.ConnectionString);
            await migrateWithoutKey.Should().ThrowAsync<InvalidOperationException>();
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var retained = new NpgsqlCommand(
                "SELECT simulation_patient_reference_legacy FROM alerts", connection);
            (await retained.ExecuteScalarAsync()).Should().Be("SIM-PAT-LEGACY");
        }

        await DatabaseOperations.MigrateAsync(fixture.ConnectionString, key);
        // Re-running completion must preserve the ciphertext and avoid duplicate history.
        await using var verify = DatabaseOperations.CreateContext(fixture.ConnectionString);
        var alert = await verify.Alerts.SingleAsync();
        var encryptedPatient = alert.SimulationPatientReference.Ciphertext.ToArray();
        protector.Unprotect(alert.SimulationPatientReference,
            new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, organizationId.Value))
            .Should().Be("SIM-PAT-LEGACY");
        protector.Unprotect(alert.OriginalSource!, sourceContext).Should().Be("SIMULATION: original legacy source");
        var revision = await verify.AlertSourceRevisions.SingleAsync();
        revision.AlertId.Should().Be(alertId);
        revision.OrganizationId.Should().Be(organizationId);
        revision.AlertVersion.Value.Should().Be(1);
        revision.CreatedByUserId.Should().Be(userId);
        revision.CreatedAtUtc.Should().Be(CreatedAt);
        protector.Unprotect(revision.Source, sourceContext).Should().Be("SIMULATION: original legacy source");
        alert.DraftVersion.Value.Should().Be(3);

        await DatabaseOperations.MigrateAsync(fixture.ConnectionString, key);
        await using var replay = DatabaseOperations.CreateContext(fixture.ConnectionString);
        (await replay.AlertSourceRevisions.CountAsync()).Should().Be(1);
        (await replay.Alerts.SingleAsync()).SimulationPatientReference.Ciphertext.Should().Equal(encryptedPatient);
        await using var schemaConnection = new NpgsqlConnection(fixture.ConnectionString);
        await schemaConnection.OpenAsync();
        await using var schema = new NpgsqlCommand("""
            SELECT column_name, is_nullable FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'alerts'
                AND column_name LIKE 'simulation_patient_reference%'
            """, schemaConnection);
        await using var reader = await schema.ExecuteReaderAsync();
        var columns = new Dictionary<string, string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0), reader.GetString(1));
        }

        columns.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["simulation_patient_reference_ciphertext"] = "NO",
            ["simulation_patient_reference_key_version"] = "NO",
            ["simulation_patient_reference_purpose"] = "NO",
        });
    }
}
