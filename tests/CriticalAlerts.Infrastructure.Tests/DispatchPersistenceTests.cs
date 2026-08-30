using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class DispatchPersistenceTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

    [Fact]
    public async Task ScenarioSettingsAreOrganizationAndChannelScopedWithSafeDefault()
    {
        await using var db = fixture.CreateContext();
        var store = new SimulationDispatchScenarioStore(db);

        (await store.GetAsync(DemoDataSeeder.OrganizationId, NotificationChannel.Sms, CancellationToken.None))
            .Should().Be(SimulationDispatchScenario.ImmediateSuccess);

        await store.SetAsync(
            DemoDataSeeder.OrganizationId,
            NotificationChannel.Sms,
            SimulationDispatchScenario.SmsFailure,
            DemoDataSeeder.MorganUserId,
            Now,
            CancellationToken.None);

        (await store.GetAsync(DemoDataSeeder.OrganizationId, NotificationChannel.Sms, CancellationToken.None))
            .Should().Be(SimulationDispatchScenario.SmsFailure);
        (await store.GetAsync(OrganizationId.New(), NotificationChannel.Sms, CancellationToken.None))
            .Should().Be(SimulationDispatchScenario.ImmediateSuccess);

        await store.ResetAsync(
            DemoDataSeeder.OrganizationId,
            NotificationChannel.Sms,
            DemoDataSeeder.MorganUserId,
            Now,
            CancellationToken.None);

        (await store.GetAsync(DemoDataSeeder.OrganizationId, NotificationChannel.Sms, CancellationToken.None))
            .Should().Be(SimulationDispatchScenario.ImmediateSuccess);
        (await db.SimulationDispatchScenarioSettings.CountAsync()).Should().Be(0);
    }
}
