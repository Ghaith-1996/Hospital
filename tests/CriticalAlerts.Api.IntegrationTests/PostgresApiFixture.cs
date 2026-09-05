using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

public sealed class PostgresApiFixture : IAsyncLifetime
{
    private const string Image = "postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636";

    public PostgreSqlContainer Container { get; private set; } = null!;

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Container = new PostgreSqlBuilder(Image)
            .WithDatabase("critical_alerts_test_api")
            .WithUsername("critical_alerts_api_test")
            .WithPassword(password)
            .Build();

        await Container.StartAsync();
    }

    public Task DisposeAsync() => Container is null ? Task.CompletedTask : Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres-api")]
public sealed class PostgresApiCollection : ICollectionFixture<PostgresApiFixture>
{
    public const string Name = "postgres-api";
}
