using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CriticalAlerts.Infrastructure.Persistence;

/// <summary>
/// Completes the one-way transition from the retired plaintext patient-reference column.
/// EF migrations cannot encrypt values because the key is application configuration, so this
/// step runs immediately after the schema migration and drops the legacy column in the same
/// database transaction.
/// </summary>
internal static class SensitiveDataMigration
{
    private const string LegacyColumn = "simulation_patient_reference_legacy";

    public static async Task CompleteAsync(
        CriticalAlertsDbContext db,
        string? dataProtectionKey,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        if (!await ColumnExistsAsync(connection, LegacyColumn, cancellationToken))
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var patientRows = await ReadPatientReferencesAsync(connection, transaction, cancellationToken);
        var protector = patientRows.Count == 0
            ? null
            : AesGcmSensitiveDataProtector.FromBase64(dataProtectionKey);
        foreach (var row in patientRows)
        {
            var protectedValue = protector!.Protect(
                row.Plaintext,
                new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, row.OrganizationId));
            await using var update = new NpgsqlCommand(
                """
                UPDATE alerts
                SET simulation_patient_reference_ciphertext = @ciphertext,
                    simulation_patient_reference_key_version = @key_version,
                    simulation_patient_reference_purpose = @purpose
                WHERE id = @id AND organization_id = @organization_id;
                """,
                connection,
                transaction);
            update.Parameters.Add(new NpgsqlParameter("ciphertext", NpgsqlDbType.Bytea) { Value = protectedValue.Ciphertext });
            update.Parameters.AddWithValue("key_version", protectedValue.KeyVersion);
            update.Parameters.AddWithValue("purpose", protectedValue.Purpose);
            update.Parameters.AddWithValue("id", row.AlertId);
            update.Parameters.AddWithValue("organization_id", row.OrganizationId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        if (patientRows.Count == 0 && await HasAlertsAsync(connection, transaction, cancellationToken))
        {
            throw new InvalidOperationException(
                "The patient-reference migration found existing alerts without a legacy patient reference; the legacy column was retained and no data was changed.");
        }

        await BackfillSourceRevisionsAsync(connection, transaction, cancellationToken);
        await using (var finalize = new NpgsqlCommand(
            """
            ALTER TABLE alerts
                ALTER COLUMN simulation_patient_reference_ciphertext SET NOT NULL,
                ALTER COLUMN simulation_patient_reference_key_version SET NOT NULL,
                ALTER COLUMN simulation_patient_reference_purpose SET NOT NULL;
            ALTER TABLE alerts DROP COLUMN simulation_patient_reference_legacy;
            """,
            connection,
            transaction))
        {
            await finalize.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        NpgsqlConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND table_name = 'alerts'
                  AND column_name = @column_name);
            """,
            connection);
        command.Parameters.AddWithValue("column_name", columnName);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> HasAlertsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM alerts);", connection, transaction);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<List<PatientReferenceRow>> ReadPatientReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var rows = new List<PatientReferenceRow>();
        await using var command = new NpgsqlCommand(
            $"SELECT id, organization_id, {LegacyColumn} FROM alerts ORDER BY id;",
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2))
            {
                throw new InvalidOperationException(
                    "The patient-reference migration found a null legacy value; the legacy column was retained and no data was changed.");
            }

            rows.Add(new PatientReferenceRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }

        return rows;
    }

    private static async Task BackfillSourceRevisionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, organization_id, source_type, original_source_ciphertext,
                   original_source_key_version, original_source_purpose, created_by_user_id, created_at_utc
            FROM alerts
            WHERE original_source_ciphertext IS NOT NULL
            ORDER BY id;
            """,
            connection,
            transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SourceRevisionRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SourceRevisionRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                (byte[])reader.GetValue(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetGuid(6),
                reader.GetFieldValue<DateTimeOffset>(7)));
        }

        await reader.CloseAsync();
        foreach (var row in rows)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO alert_source_revisions (
                    id, organization_id, alert_id, alert_version, source_type,
                    source_ciphertext, source_key_version, source_purpose,
                    created_by_user_id, created_at_utc)
                SELECT @id, @organization_id, @alert_id, 1, @source_type,
                       @source_ciphertext, @source_key_version, @source_purpose,
                       @created_by_user_id, @created_at_utc
                WHERE NOT EXISTS (
                    SELECT 1 FROM alert_source_revisions
                    WHERE alert_id = @alert_id AND alert_version = 1);
                """,
                connection,
                transaction);
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue("organization_id", row.OrganizationId);
            insert.Parameters.AddWithValue("alert_id", row.AlertId);
            insert.Parameters.AddWithValue("source_type", row.SourceType);
            insert.Parameters.Add(new NpgsqlParameter("source_ciphertext", NpgsqlDbType.Bytea) { Value = row.Ciphertext });
            insert.Parameters.AddWithValue("source_key_version", row.KeyVersion);
            insert.Parameters.AddWithValue("source_purpose", row.Purpose);
            insert.Parameters.AddWithValue("created_by_user_id", row.CreatedByUserId);
            insert.Parameters.AddWithValue("created_at_utc", row.CreatedAtUtc);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed record PatientReferenceRow(Guid AlertId, Guid OrganizationId, string Plaintext);

    private sealed record SourceRevisionRow(
        Guid AlertId,
        Guid OrganizationId,
        string SourceType,
        byte[] Ciphertext,
        string KeyVersion,
        string Purpose,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAtUtc);
}
