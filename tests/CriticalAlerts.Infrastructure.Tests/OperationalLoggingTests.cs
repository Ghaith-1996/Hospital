using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class OperationalLoggingTests(MigratedPostgresFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyCommittedAuditOperationsEmitAndMetadataNeverReachesLogger(bool rollback)
    {
        var capture = new Capture();
        var observer = new CommittedOperations(capture);
        await using var db = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(new OperationSaveInterceptor(observer), new OperationTransactionInterceptor(observer)).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        const string sentinel = "SIM-APPROVED-MESSAGE-DO-NOT-LOG";
        var correlation = Guid.NewGuid().ToString("N");
        foreach (var action in new[] { "alert.confirmed", "directory.import.applied", "dispatch.failed",
            "recipient.response.accepted", "alert.resolved", "audit.read", "escalation-recipients-activated", "escalation-processing-failed" })
            db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), DemoDataSeeder.OrganizationId, "user",
                DemoDataSeeder.JordanUserId, action, "alert", Guid.NewGuid(), "succeeded", correlation,
                System.Text.Json.JsonSerializer.Serialize(new { message = sentinel, channel = sentinel }), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        capture.Entries.Should().BeEmpty();
        if (rollback) await transaction.RollbackAsync(); else await transaction.CommitAsync();
        capture.Entries.Count.Should().Be(rollback ? 0 : 8);
        string.Join(" ", capture.Entries).Contains(sentinel, StringComparison.Ordinal).Should().BeFalse();
        if (!rollback) string.Join(" ", capture.Entries).Should().Contain(correlation);
    }

    [Fact]
    public async Task ImplicitCommitEmitsOnceAndUnknownStringsAreNotLogged()
    {
        var capture = new Capture();
        var observer = new CommittedOperations(capture);
        await using var db = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(new OperationSaveInterceptor(observer), new OperationTransactionInterceptor(observer)).Options);
        const string sentinel = "SIM-PATIENT-PHASE10-SENTINEL";
        db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), DemoDataSeeder.OrganizationId, "user",
            DemoDataSeeder.JordanUserId, sentinel, "alert", Guid.NewGuid(), "succeeded", sentinel, "{}", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();
        capture.Entries.Should().HaveCount(1);
        capture.Entries[0].Contains(sentinel, StringComparison.Ordinal).Should().BeFalse();
    }

    internal sealed class Capture : ILogger
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            exception.Should().BeNull();
            var properties = state is IEnumerable<KeyValuePair<string, object?>> fields
                ? string.Join(" ", fields.Select(f => f.Key + "=" + f.Value)) : "";
            Entries.Add(formatter(state, exception) + properties);
        }
    }
}
