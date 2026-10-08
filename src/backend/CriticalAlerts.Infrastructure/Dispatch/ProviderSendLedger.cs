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
    /// <summary>
    /// Returns the durable entry for this attempt key. <see cref="ProviderSendLedgerEntry.Inserted"/> is true only for the
    /// first invocation; a later call (a replay, or a recreated attempt after a crash) gets the originally recorded values.
    /// </summary>
    public static async Task<ProviderSendLedgerEntry> GetOrRecordAsync(
        CriticalAlertsDbContext db,
        OrganizationId organizationId,
        string provider,
        string attemptIdempotencyKey,
        DateTimeOffset nowUtc,
        ProviderSendBinding? binding,
        CancellationToken cancellationToken)
    {
        if (nowUtc.Offset != TimeSpan.Zero)
            throw new DispatchValidationException("clock-not-utc", "The provider send ledger requires a UTC clock.");
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("The provider send ledger requires the database connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            WITH inserted AS (
                INSERT INTO provider_send_ledger (id, organization_id, provider, attempt_idempotency_key, first_sent_at_utc,
                    callback_tag, operation_fingerprint)
                VALUES (@id, @organization, @provider, @key, @now, @tag, @fingerprint)
                ON CONFLICT (organization_id, provider, attempt_idempotency_key) DO NOTHING
                RETURNING first_sent_at_utc, operation_fingerprint, true AS inserted)
            SELECT first_sent_at_utc, operation_fingerprint, inserted FROM inserted
            UNION ALL
            SELECT first_sent_at_utc, operation_fingerprint, false FROM provider_send_ledger
            WHERE organization_id = @organization AND provider = @provider AND attempt_idempotency_key = @key
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("organization", organizationId.Value);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("key", attemptIdempotencyKey);
        command.Parameters.AddWithValue("now", nowUtc);
        command.Parameters.Add(new NpgsqlParameter("tag", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)binding?.CallbackTag ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("fingerprint", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)binding?.OperationFingerprint ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The provider send ledger did not return a first-send time.");
        var recorded = reader.GetValue(0) switch
        {
            DateTime value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)),
            DateTimeOffset value => value.ToUniversalTime(),
            _ => throw new InvalidOperationException("The provider send ledger did not return a first-send time."),
        };
        return new ProviderSendLedgerEntry(recorded, reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2));
    }
}

internal sealed record ProviderSendLedgerEntry(DateTimeOffset FirstSentAtUtc, string? OperationFingerprint, bool Inserted);
