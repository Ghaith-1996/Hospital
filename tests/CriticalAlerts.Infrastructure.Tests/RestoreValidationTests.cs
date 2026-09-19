using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class RestoreValidationTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task MigratedFictionalDatabaseHasStableReadableStructuralInvariants()
    {
        var before = await RestoreValidation.ValidateAsync(fixture.ConnectionString, "Test");
        var after = await RestoreValidation.ValidateAsync(fixture.ConnectionString, "Test");
        after.Should().BeEquivalentTo(before);
        before.Counts["organizations"].Should().BeGreaterThan(0);
        before.Counts.Should().ContainKeys("audit_events", "outbox_messages", "escalation_runs", "alert_escalation_plans");
        before.Migrations.Should().Contain("20260919150045_Phase10AuditProtection");
    }

    [Theory]
    [InlineData(null, "critical_alerts_test_restore")]
    [InlineData("Production", "critical_alerts_test_restore")]
    [InlineData("Staging", "critical_alerts_test_restore")]
    [InlineData("Unknown", "critical_alerts_test_restore")]
    [InlineData("Test", "hospital")]
    [InlineData("Test", "postgres")]
    [InlineData("Test", "critical_alerts_test;bad")]
    public void UnsafeRestoreTargetIsRejectedWithoutReflectingInput(string? environment, string database)
    {
        var act = () => RestoreValidation.EnsureSafeTarget(environment, "127.0.0.1", database);
        act.Should().Throw<InvalidOperationException>().WithMessage("Restore validation requires a local Development/Test simulation database.");
    }

    [Fact]
    public void RemoteRestoreTargetIsRejected()
    {
        var act = () => RestoreValidation.EnsureSafeTarget("Test", "remote.example.invalid", "critical_alerts_test");
        act.Should().Throw<InvalidOperationException>();
    }
}
