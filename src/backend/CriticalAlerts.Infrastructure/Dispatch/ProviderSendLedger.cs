using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Records the first-send time of a provider attempt on its own connection, committed before the network call.
/// The dispatch transaction holds the alert lock and may roll back after the provider accepted the send; this
/// row survives that, so a recreated attempt replays the identical repeatable request. It only references the
/// organization (never locked for update), so the separate write cannot wait on the dispatch transaction.
/// </summary>
internal static class ProviderSendLedger
{
    public static async Task<DateTimeOffset> GetOrRecordFirstSendAsync(
        CriticalAlertsDbContext db,
        OrganizationId organizationId,
        string provider,
        string attemptIdempotencyKey,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The provider send ledger requires the database connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH inserted AS (
                INSERT INTO provider_send_ledger (id, organization_id, provider, attempt_idempotency_key, first_sent_at_utc)
                VALUES (@id, @organization, @provider, @key, @now)
                ON CONFLICT (organization_id, provider, attempt_idempotency_key) DO NOTHING
                RETURNING first_sent_at_utc)
            SELECT first_sent_at_utc FROM inserted
            UNION ALL
            SELECT first_sent_at_utc FROM provider_send_ledger
            WHERE organization_id = @organization AND provider = @provider AND attempt_idempotency_key = @key
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("organization", organizationId.Value);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("key", attemptIdempotencyKey);
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new DispatchValidationException("clock-not-utc", "The provider send ledger requires a UTC clock.");
        command.Parameters.AddWithValue("now", nowUtc);
        var recorded = await command.ExecuteScalarAsync(cancellationToken);
        return recorded switch
        {
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            DateTimeOffset value => value.ToUniversalTime(),
            _ => throw new InvalidOperationException("The provider send ledger did not return a first-send time."),
        };
    }
}
