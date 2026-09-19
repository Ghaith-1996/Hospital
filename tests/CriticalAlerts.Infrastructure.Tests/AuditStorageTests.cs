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

    [Fact]
    public async Task ApplicationRoleCanAppendButCannotUpdateOrDeleteEvenWithTableGrants()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = new NpgsqlCommand("""
            CREATE ROLE phase10_audit_runtime NOLOGIN;
            GRANT USAGE ON SCHEMA public TO phase10_audit_runtime;
            GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON audit_events TO phase10_audit_runtime;
            SET ROLE phase10_audit_runtime;
            """, connection)) await setup.ExecuteNonQueryAsync();
        var id = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO audit_events (id, organization_id, actor_type, action, resource_type, resource_id,
                outcome, correlation_id, sanitized_metadata, occurred_at_utc)
            VALUES (@id, @organization, 'user', 'audit.read', 'audit', @id, 'succeeded', @correlation, '{}', CURRENT_TIMESTAMP)
            """, connection))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("organization", DemoDataSeeder.OrganizationId.Value);
            insert.Parameters.AddWithValue("correlation", Guid.NewGuid().ToString("N"));
            (await insert.ExecuteNonQueryAsync()).Should().Be(1);
        }
        foreach (var sql in new[] { "UPDATE audit_events SET outcome = 'changed' WHERE id = @id", "DELETE FROM audit_events WHERE id = @id" })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var mutation = new NpgsqlCommand(sql, connection, transaction);
            mutation.Parameters.AddWithValue("id", id);
            var rejected = await Record.ExceptionAsync(() => mutation.ExecuteNonQueryAsync());
            await transaction.RollbackAsync();
            rejected.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        }
    }

    [Fact]
    public async Task QueryIndexesSupportStableScopedTraversalWithoutRetainingRedundantTimeIndex()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'audit_events'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var definitions = new List<string>();
        while (await reader.ReadAsync()) definitions.Add(reader.GetString(0));
        definitions.Should().Contain(d => d.EndsWith("(organization_id, occurred_at_utc, id)", StringComparison.Ordinal));
        definitions.Should().Contain(d => d.EndsWith("(organization_id, action, occurred_at_utc, id)", StringComparison.Ordinal));
        definitions.Should().Contain(d => d.EndsWith("(organization_id, resource_type, occurred_at_utc, id)", StringComparison.Ordinal));
        definitions.Should().Contain(d => d.EndsWith("(organization_id, correlation_id, occurred_at_utc, id)", StringComparison.Ordinal));
        definitions.Should().NotContain(d => d.EndsWith("(organization_id, occurred_at_utc)", StringComparison.Ordinal));
    }

    private static AuditEvent NewEvent() => AuditEvent.Record(AuditEventId.New(), DemoDataSeeder.OrganizationId,
        "user", DemoDataSeeder.JordanUserId, "audit.read", "audit", Guid.NewGuid(), "succeeded",
        Guid.NewGuid().ToString("N"), "{}", DateTimeOffset.UtcNow);
}
