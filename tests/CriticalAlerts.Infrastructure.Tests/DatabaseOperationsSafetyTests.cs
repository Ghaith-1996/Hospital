using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class DatabaseOperationsSafetyTests
{
    [Fact]
    public void DemoResetRequiresExplicitConfirmation()
    {
        var act = () => DatabaseOperations.EnsureDemoResetTarget(
            "Host=127.0.0.1;Database=critical_alerts_test;Username=test;Password=test",
            confirmReset: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*explicit confirmation*no database was changed*");
    }

    [Theory]
    [InlineData("Host=db.example.test;Database=critical_alerts_test;Username=test;Password=test")]
    [InlineData("Host=127.0.0.1;Database=production;Username=test;Password=test")]
    [InlineData("Host=127.0.0.1;Database=critical_alerts_production;Username=test;Password=test")]
    [InlineData("Host=127.0.0.1;Database=critical_alerts_test!;Username=test;Password=test")]
    public void DemoResetRejectsRemoteHostsAndNonDemoDatabaseNames(string connectionString)
    {
        var act = () => DatabaseOperations.EnsureDemoResetTarget(connectionString, confirmReset: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no database was changed*");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void ConfirmedDemoResetAcceptsLoopbackDemoTargets(string host)
    {
        var act = () => DatabaseOperations.EnsureDemoResetTarget(
            $"Host={host};Database=critical_alerts_test;Username=test;Password=test",
            confirmReset: true);

        act.Should().NotThrow();
    }
}
