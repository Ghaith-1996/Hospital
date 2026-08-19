using System.Security.Cryptography;
using Testcontainers.PostgreSql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Image = "postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636";

    public PostgreSqlContainer Container { get; private set; } = null!;

    public string ConnectionString => Container.GetConnectionString();

    public async Task InitializeAsync()
    {
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Container = new PostgreSqlBuilder(Image)
            .WithDatabase("critical_alerts_test")
            .WithUsername("critical_alerts_test")
            .WithPassword(password)
            .Build();

        await Container.StartAsync();
    }

    public Task DisposeAsync() => Container is null ? Task.CompletedTask : Container.DisposeAsync().AsTask();
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
