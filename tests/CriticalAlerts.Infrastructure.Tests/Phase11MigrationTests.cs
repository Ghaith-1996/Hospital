using System.Security.Cryptography;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Assistance;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class Phase11MigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task UpgradePreservesPhase10SourceAndCompositeForeignKeyRejectsWrongVersion()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = AesGcmSensitiveDataProtector.FromBase64(key);
        var org = DemoDataSeeder.OrganizationId;
        const string source = "SIMULATION: historical typed source";
        var alert = Alert.CreateDraft(AlertId.New(), org, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId, "SIM-PAT-UPGRADE", protector.Protect("SIM-PAT-UPGRADE", new(ProtectedValuePurposes.AlertPatientReference, org.Value)),
            "Simulation room", "DEMO", AlertSourceType.Typed, protector.Protect(source, new(ProtectedValuePurposes.AlertTypedSource, org.Value)), DateTimeOffset.UtcNow);
        await DatabaseOperations.MigrateAsync(fixture.ConnectionString, key);
        await using (var legacy = DatabaseOperations.CreateContext(fixture.ConnectionString))
        {
            await legacy.GetService<IMigrator>().MigrateAsync("20260919150045_Phase10AuditProtection");
            await new DemoDataSeeder(legacy, key).SeedAsync();
            legacy.Alerts.Add(alert); await legacy.SaveChangesAsync();
        }
        await DatabaseOperations.MigrateAsync(fixture.ConnectionString, key);
        await using var db = DatabaseOperations.CreateContext(fixture.ConnectionString);
        var stored = await db.Alerts.Include(row => row.SourceRevisions).SingleAsync(row => row.Id == alert.Id);
        stored.DraftVersion.Should().Be(alert.DraftVersion);
        protector.Unprotect(stored.CurrentSourceRevision!.Source, new(ProtectedValuePurposes.AlertTypedSource, org.Value)).Should().Be(source);
        (await db.AssistanceResults.CountAsync()).Should().Be(0);
        var kind = AssistanceKind.Transcription;
        db.AssistanceResults.Add(AssistanceResult.Create(Guid.NewGuid(), org, alert.Id, new(2), alert.CurrentSourceRevision!.Id,
            DemoDataSeeder.JordanUserId, kind, "Simulated", "DEMO-1", "DEMO-1",
            protector.Protect("SIMULATION: mismatched version", new(AssistanceResult.PurposeFor(kind), org.Value)), DateTimeOffset.UtcNow));
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        failure.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }
}
