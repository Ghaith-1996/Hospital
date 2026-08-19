using FluentAssertions;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PostgresConnectivityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task StartsPostgres18Container()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var version = await ReadScalarAsync(connection, "SELECT current_setting('server_version')");

        version.Should().StartWith("18.");
    }

    [Fact]
    public async Task CanOpenRealPostgresConnection()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var result = await ReadScalarAsync(connection, "SELECT 1");

        result.Should().Be("1");
    }

    [Fact]
    public async Task DoesNotCreateSchemaInPhase1()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        var result = await ReadScalarAsync(
            connection,
            "SELECT count(*)::text FROM pg_catalog.pg_tables WHERE schemaname = 'public'");

        result.Should().Be("0");
    }

    private static async Task<string> ReadScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value?.ToString() ?? string.Empty;
    }
}
