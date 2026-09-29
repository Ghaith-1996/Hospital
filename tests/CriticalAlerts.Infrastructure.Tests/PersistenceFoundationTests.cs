using System.Security.Cryptography;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class PersistenceFoundationTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T16:00:00Z");

    [Fact]
    public async Task SensitiveAlertColumnsAndSourceRevisionHistoryAreProtected()
    {
        await using var db = fixture.CreateContext();
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-SCHEMA",
            protector.Protect(
                "SIM-PAT-SCHEMA",
                new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value)),
            "North Wing / Schema Protection Room",
            "DEMO-URGENT",
            AlertSourceType.Typed,
            protector.Protect(
                "SIMULATION: original schema source",
                new SensitiveDataContext(ProtectedValuePurposes.AlertTypedSource, DemoDataSeeder.OrganizationId.Value)),
            Now);
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        alert.UpdateSource(
            protector.Protect(
                "SIMULATION: corrected schema source",
                new SensitiveDataContext(ProtectedValuePurposes.AlertTypedSource, DemoDataSeeder.OrganizationId.Value)),
            alert.DraftVersion,
            Now.AddMinutes(1));
        await db.SaveChangesAsync();

        (await db.AlertSourceRevisions
                .Where(revision => revision.AlertId == alert.Id)
                .Select(revision => revision.AlertVersion.Value)
                .ToArrayAsync())
            .Should().BeEquivalentTo([1, 2]);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_name, column_name, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (
                  (table_name = 'alerts' AND column_name LIKE 'simulation_patient_reference%')
                  OR (table_name = 'alert_source_revisions' AND column_name LIKE 'source_%')
              );
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            columns[$"{reader.GetString(0)}.{reader.GetString(1)}"] = reader.GetString(2);
        }

        columns.Should().NotContainKey("alerts.simulation_patient_reference");
        columns["alerts.simulation_patient_reference_ciphertext"].Should().Be("NO");
        columns["alerts.simulation_patient_reference_key_version"].Should().Be("NO");
        columns["alerts.simulation_patient_reference_purpose"].Should().Be("NO");
        columns["alert_source_revisions.source_ciphertext"].Should().Be("NO");
        columns["alert_source_revisions.source_key_version"].Should().Be("NO");
        columns["alert_source_revisions.source_purpose"].Should().Be("NO");
    }

    [Fact]
    public async Task OrganizationIsolationRejectsCrossOrganizationSite()
    {
        await using var db = fixture.CreateContext();
        var foreign = Organization.CreateSimulation(OrganizationId.New(), "Fictional Other Simulation Hospital", Now);
        db.Organizations.Add(foreign);
        await db.SaveChangesAsync();

        db.Departments.Add(Department.Create(DepartmentId.New(), foreign.Id, DemoDataSeeder.NorthSiteId, "Foreign dept", "SIM-DEPT-FOREIGN", Now));
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RecipientUniquenessIsEnforced()
    {
        await using var db = fixture.CreateContext();
        var alert = await CreatePersistedAlertAsync(db);
        var maya = await db.Practitioners.SingleAsync(practitioner => practitioner.Id == DemoDataSeeder.MayaChenId);
        alert.ReplaceRecipients(
            [RecipientSelection(maya, NotificationChannel.SecureMessage)],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now);
        await db.SaveChangesAsync();

        var duplicate = new AlertRecipientSelection(
            AlertRecipientSelectionId.New(),
            alert.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            maya.Id,
            null,
            NotificationChannel.SecureMessage,
            DemoDataSeeder.JordanUserId,
            Now,
            "SIM-REV-PERSIST",
            Now,
            "On-call not displayed");
        db.AlertRecipientSelections.Add(duplicate);
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RecipientSelectionsMayRepeatPractitionerAndChannelAcrossVersions()
    {
        await using var db = fixture.CreateContext();
        var alert = await CreatePersistedAlertAsync(db);
        var maya = await db.Practitioners.SingleAsync(practitioner => practitioner.Id == DemoDataSeeder.MayaChenId);

        alert.ReplaceRecipients(
            [RecipientSelection(maya, NotificationChannel.SecureMessage)],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now);
        alert.ReplaceRecipients(
            [RecipientSelection(maya, NotificationChannel.SecureMessage)],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now.AddMinutes(1));
        await db.SaveChangesAsync();

        (await db.AlertRecipientSelections.CountAsync(selection => selection.AlertId == alert.Id))
            .Should().Be(2);
        (await db.AlertRecipientSelections
                .Where(selection => selection.AlertId == alert.Id)
                .Select(selection => selection.AlertVersion.Value)
                .ToArrayAsync())
            .Should().BeEquivalentTo([2, 3]);
    }

    [Theory]
    [InlineData(false)]
    public async Task OptimisticConcurrencyRejectsStaleWrite(bool saveSynchronously)
    {
        await using var first = fixture.CreateContext();
        var created = await CreatePersistedAlertAsync(first);

        await using var second = fixture.CreateContext();
        var left = await second.Alerts.Include(alert => alert.StateTransitions).SingleAsync(alert => alert.Id == created.Id);
        await using var third = fixture.CreateContext();
        var right = await third.Alerts.Include(alert => alert.StateTransitions).SingleAsync(alert => alert.Id == created.Id);

        left.UpdateSource(Protect("SIMULATION: first writer"), left.DraftVersion, Now);
        await second.SaveChangesAsync();

        right.UpdateSource(Protect("SIMULATION: second writer"), right.DraftVersion, Now);
        var staleAuditId = AuditEventId.New();
        third.AuditEvents.Add(AuditEvent.Record(
            staleAuditId, right.OrganizationId, "user", DemoDataSeeder.JordanUserId,
            "alert.edited", "alert", right.Id.Value, "succeeded", "corr-stale-source", "{}", Now));
        var act = async () =>
        {
            if (saveSynchronously)
            {
                third.SaveChanges();
            }
            else
            {
                await third.SaveChangesAsync();
            }
        };
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verification = fixture.CreateContext();
        var persisted = await verification.Alerts.Include(alert => alert.SourceRevisions)
            .SingleAsync(alert => alert.Id == created.Id);
        persisted.DraftVersion.Value.Should().Be(2);
        persisted.SourceRevisions.Should().HaveCount(2);
        persisted.SourceRevisions.Single(revision => revision.AlertVersion.Value == 2)
            .Source.Should().BeEquivalentTo(Protect("SIMULATION: first writer"));
        (await verification.AuditEvents.AnyAsync(audit => audit.Id == staleAuditId)).Should().BeFalse();
    }

    [Fact]
    public async Task IdempotencyKeysAreUniquePerOrganizationAndOperation()
    {
        await using var db = fixture.CreateContext();
        db.IdempotencyRecords.Add(IdempotencyRecord.Start(
            IdempotencyRecordId.New(),
            DemoDataSeeder.OrganizationId,
            "ConfirmAndDispatchAlert",
            "same-key",
            "hash-1",
            Now));
        await db.SaveChangesAsync();

        db.IdempotencyRecords.Add(IdempotencyRecord.Start(
            IdempotencyRecordId.New(),
            DemoDataSeeder.OrganizationId,
            "ConfirmAndDispatchAlert",
            "same-key",
            "hash-2",
            Now));
        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task FieldConfirmationUniquenessIsEnforcedPerAlertVersionAndField()
    {
        await using var db = fixture.CreateContext();
        var alert = await CreatePersistedAlertAsync(db);
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", DemoDataSeeder.JordanUserId, alert.DraftVersion, Now);
        await db.SaveChangesAsync();

        var duplicateId = Guid.NewGuid();
        var act = async () => await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO alert_field_confirmations (
                id, organization_id, alert_id, alert_version, field_id, original_value, normalized_value, unit, status, confirmed_by_user_id, confirmed_at_utc)
            VALUES (
                {duplicateId},
                {alert.OrganizationId.Value},
                {alert.Id.Value},
                {alert.DraftVersion.Value},
                {"heartRate"},
                {"118"},
                {"118"},
                {"beats/min"},
                {"Confirmed"},
                {DemoDataSeeder.JordanUserId.Value},
                {Now});
            """);
        await act.Should().ThrowAsync<PostgresException>().Where(exception => exception.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public void DemoResetRefusesStagingAndProduction()
    {
        var actStaging = () => DatabaseOperations.EnsureEnvironmentAllowed("Staging");
        var actProduction = () => DatabaseOperations.EnsureEnvironmentAllowed("Production");
        actStaging.Should().Throw<InvalidOperationException>().WithMessage("*no database was changed*");
        actProduction.Should().Throw<InvalidOperationException>().WithMessage("*no database was changed*");
    }

    private async Task<Alert> CreatePersistedAlertAsync(CriticalAlertsDbContext db)
    {
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            "SIM-PAT-0099",
            ProtectPatient("SIM-PAT-0099"),
            "North Wing / Sim Unit 2 / Room 204",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: fictional note for persistence test."),
            Now);
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    }

    private static ValidatedRecipientSelection RecipientSelection(
        CriticalAlerts.Domain.Directory.Practitioner practitioner,
        NotificationChannel channel)
        => new(
            practitioner.Id,
            null,
            channel,
            "SIM-REV-PERSIST",
            Now,
            "On-call not displayed");

    private static ProtectedValue Protect(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text), "test-v1", "alert-source");

    private static ProtectedValue ProtectPatient(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text), "test-v1", ProtectedValuePurposes.AlertPatientReference);
}

public sealed class MigratedPostgresFixture : IAsyncLifetime
{
    private readonly PostgresFixture inner = new();
    private string dataProtectionKey = string.Empty;

    public string ConnectionString => inner.ConnectionString;

    public string DataProtectionKey => dataProtectionKey;

    public async Task InitializeAsync()
    {
        await inner.InitializeAsync();
        dataProtectionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await DatabaseOperations.ResetDemoAsync(inner.ConnectionString, "Test", dataProtectionKey, confirmReset: true);
    }

    public CriticalAlertsDbContext CreateContext() => DatabaseOperations.CreateContext(ConnectionString);

    public Task ResetAsync() => DatabaseOperations.ResetDemoAsync(ConnectionString, "Test", dataProtectionKey, confirmReset: true);

    public Task DisposeAsync() => inner.DisposeAsync();
}

[CollectionDefinition(MigratedPostgresCollection.Name)]
public sealed class MigratedPostgresCollection : ICollectionFixture<MigratedPostgresFixture>
{
    public const string Name = "postgres-migrated";
}
