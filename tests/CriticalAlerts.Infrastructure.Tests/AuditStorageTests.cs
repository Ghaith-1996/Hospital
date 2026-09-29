using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class AuditStorageTests(MigratedPostgresFixture fixture)
{
    [Theory]
    [InlineData("UPDATE audit_events SET outcome = 'changed' WHERE id = @id")]
    [InlineData("DELETE FROM audit_events WHERE id = @id")]
    [InlineData("TRUNCATE audit_events")]
    public async Task DirectDatabaseMutationIsRejectedAndOriginalEvidenceSurvives(string sql)
    {
        await using var db = fixture.CreateContext();
        var row = NewEvent();
        db.AuditEvents.Add(row);
        await db.SaveChangesAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", row.Id.Value);
        var rejected = await Record.ExceptionAsync(() => command.ExecuteNonQueryAsync());
        await transaction.RollbackAsync();
        rejected.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        db.ChangeTracker.Clear();
        (await db.AuditEvents.SingleAsync(e => e.Id == row.Id)).Outcome.Should().Be("succeeded");
    }

    private static AuditEvent NewEvent() => AuditEvent.Record(AuditEventId.New(), DemoDataSeeder.OrganizationId,
        "user", DemoDataSeeder.JordanUserId, "audit.read", "audit", Guid.NewGuid(), "succeeded",
        Guid.NewGuid().ToString("N"), "{}", DateTimeOffset.UtcNow);
}
