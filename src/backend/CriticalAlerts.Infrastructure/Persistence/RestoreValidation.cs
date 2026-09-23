using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Persistence;

public sealed record RestoreValidationResult(IReadOnlyList<string> Migrations, IReadOnlyDictionary<string, long> Counts);

public static partial class RestoreValidation
{
    public static void EnsureSafeTarget(string? environment, string? host, string? database)
    {
        if (environment is not ("Development" or "Test") || host is not ("127.0.0.1" or "localhost" or "::1")
            || database is null || database.Length > 63 || !SafeDatabase().IsMatch(database))
            throw new InvalidOperationException("Restore validation requires a local Development/Test simulation database.");
    }

    [GeneratedRegex("^critical_alerts_(dev|test|demo)(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeDatabase();

    public static async Task<RestoreValidationResult> ValidateAsync(string connectionString, string environment,
        CancellationToken cancellationToken = default)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        EnsureSafeTarget(environment, target.Host, target.Database);
        await using var db = DatabaseOperations.CreateContext(connectionString);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (!applied.SequenceEqual(db.Database.GetMigrations()))
            throw new InvalidOperationException("Restore schema verification failed.");
        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var entities = db.Model.GetEntityTypes().Where(e => e.GetTableName() is not null).ToArray();
        foreach (var table in entities.Select(e => e.GetTableName()!).Distinct().Order())
            counts[table] = await Scalar("SELECT count(*) FROM " + Quote(table));

        foreach (var entity in entities)
        {
            var childTable = entity.GetTableName()!;
            var childStore = StoreObjectIdentifier.Table(childTable, entity.GetSchema());
            foreach (var key in entity.GetForeignKeys())
            {
                var parent = key.PrincipalEntityType;
                if (parent.GetTableName() is not { } parentTable) continue;
                var parentStore = StoreObjectIdentifier.Table(parentTable, parent.GetSchema());
                var columns = key.Properties.Select(p => p.GetColumnName(childStore)!).ToArray();
                var principalColumns = key.PrincipalKey.Properties.Select(p => p.GetColumnName(parentStore)!).ToArray();
                var join = string.Join(" AND ", columns.Select((column, i) => "c." + Quote(column) + " = p." + Quote(principalColumns[i])));
                var present = string.Join(" AND ", columns.Select(column => "c." + Quote(column) + " IS NOT NULL"));
                var invalid = "p." + Quote(principalColumns[0]) + " IS NULL";
                if (entity.FindProperty("OrganizationId") is { } childOrganization && parent.FindProperty("OrganizationId") is { } parentOrganization)
                    invalid += " OR c." + Quote(childOrganization.GetColumnName(childStore)!) + " <> p." + Quote(parentOrganization.GetColumnName(parentStore)!);
                if (await Scalar("SELECT count(*) FROM " + Quote(childTable) + " c LEFT JOIN " + Quote(parentTable)
                    + " p ON " + join + " WHERE " + present + " AND (" + invalid + ")") != 0)
                    throw new InvalidOperationException("Restore relational verification failed.");
            }
        }

        if (await Scalar("SELECT count(*) FROM pg_constraint WHERE connamespace = 'public'::regnamespace AND contype = 'f' AND NOT convalidated") != 0
            || await Scalar("SELECT count(*) FROM pg_trigger WHERE tgrelid = 'audit_events'::regclass AND tgname = 'immutable_audit_events' AND NOT tgisinternal AND tgenabled IN ('O','A')") != 1
            || await Scalar("SELECT count(*) FROM pg_trigger WHERE tgrelid = 'alert_assistance_results'::regclass AND tgname = 'assistance_immutable' AND NOT tgisinternal AND tgenabled IN ('O','A')") != 1)
            throw new InvalidOperationException("Restore constraint verification failed.");
        await transaction.CommitAsync(cancellationToken);
        return new(applied, counts);

        async Task<long> Scalar(string sql)
        {
            // SQL identifiers come exclusively from the compiled EF model, never from input.
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
