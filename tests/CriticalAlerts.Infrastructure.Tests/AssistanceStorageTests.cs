using System.Text;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Assistance;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class AssistanceStorageTests(MigratedPostgresFixture fixture)
{
    [Theory]
    [InlineData(AssistanceKind.Transcription)]
    [InlineData(AssistanceKind.Structuring)]
    public async Task ResultIsProtectedImmutableAndBoundToExactSource(AssistanceKind kind)
    {
        const string sentinel = "SIMULATION: SIM-PHASE11-TRANSCRIPT-SECRET";
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var org = DemoDataSeeder.OrganizationId;
        var purpose = AssistanceResult.PurposeFor(kind);
        var context = new SensitiveDataContext(purpose, org.Value);
        var alert = Alert.CreateDraft(AlertId.New(), org, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId, "SIM-PAT-ASSIST", protector.Protect("SIM-PAT-ASSIST",
                new(ProtectedValuePurposes.AlertPatientReference, org.Value)), "Simulation room", "DEMO-URGENT",
            AlertSourceType.Typed, protector.Protect("SIMULATION: source", new(ProtectedValuePurposes.AlertTypedSource, org.Value)), DateTimeOffset.UtcNow);
        await using var db = fixture.CreateContext();
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var row = AssistanceResult.Create(Guid.NewGuid(), org, alert.Id, alert.DraftVersion,
            alert.CurrentSourceRevision!.Id, DemoDataSeeder.JordanUserId, kind, "Simulated", "DEMO-1", "DEMO-1",
            protector.Protect(sentinel, context), DateTimeOffset.UtcNow);
        db.AssistanceResults.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var stored = await db.AssistanceResults.SingleAsync(r => r.Id == row.Id);
        protector.Unprotect(stored.Payload, context).Should().Be(sentinel);
        Encoding.UTF8.GetString(stored.Payload.Ciphertext).Should().NotContain(sentinel);
        var foreign = () => protector.Unprotect(stored.Payload, new(purpose, Guid.NewGuid()));
        foreign.Should().Throw<Exception>();
        var wrongPurpose = () => protector.Unprotect(stored.Payload, new(ProtectedValuePurposes.AlertTypedSource, org.Value));
        wrongPurpose.Should().Throw<Exception>();
        foreach (var sql in new[] { "UPDATE alert_assistance_results SET provider = 'changed' WHERE id = @id", "DELETE FROM alert_assistance_results WHERE id = @id", "TRUNCATE alert_assistance_results" })
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", row.Id);
            var error = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());
            error.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
        db.AssistanceResults.Remove(stored);
        var removal = () => db.SaveChangesAsync();
        await removal.Should().ThrowAsync<InvalidOperationException>();
    }
}
