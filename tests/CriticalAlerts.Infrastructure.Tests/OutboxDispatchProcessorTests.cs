using System.Text;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class OutboxDispatchProcessorTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

    [Fact]
    public async Task ConfirmedAlertIsDispatchedOnceWithStableDeliveredAttempt()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alertId = await SeedConfirmedAlertAsync(db, NotificationChannel.Sms);
        var processor = CreateProcessor(db, new MutableTimeProvider(Now));

        var first = await processor.ProcessNextAsync("worker-a", CancellationToken.None);
        var second = await processor.ProcessNextAsync("worker-a", CancellationToken.None);

        first.Processed.Should().BeTrue();
        first.Rescheduled.Should().BeFalse();
        second.Processed.Should().BeFalse();
        (await db.OutboxMessages.SingleAsync(message => message.AggregateId == alertId.Value)).ProcessingState
            .Should().Be(OutboxProcessingState.Processed);
        (await db.DeliveryAttempts.Where(attempt => attempt.AlertId == alertId).ToArrayAsync())
            .Should().ContainSingle(attempt => attempt.Status == DeliveryAttemptStatus.Delivered);
        (await db.DeliveryEvents.CountAsync(item => item.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(2);
        (await db.Alerts.SingleAsync(alert => alert.Id == alertId)).State.Should().Be(AlertState.Active);
    }

    [Fact]
    public async Task DelayedDeliveryReusesTheSubmittedAttemptAfterTheOutboxBecomesDue()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alertId = await SeedConfirmedAlertAsync(db, NotificationChannel.SecureMessage);
        var clock = new MutableTimeProvider(Now);
        var store = new SimulationDispatchScenarioStore(db);
        await store.SetAsync(
            DemoDataSeeder.OrganizationId,
            NotificationChannel.SecureMessage,
            SimulationDispatchScenario.DelayedDelivery,
            DemoDataSeeder.MorganUserId,
            Now,
            CancellationToken.None);
        var processor = CreateProcessor(db, clock);

        var first = await processor.ProcessNextAsync("worker-a", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = await processor.ProcessNextAsync("worker-a", CancellationToken.None);

        first.Rescheduled.Should().BeTrue();
        second.Processed.Should().BeTrue();
        (await db.DeliveryAttempts.Where(attempt => attempt.AlertId == alertId).ToArrayAsync())
            .Should().ContainSingle(attempt => attempt.Status == DeliveryAttemptStatus.Delivered);
        (await db.DeliveryEvents.CountAsync(item => item.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(2);
    }

    [Fact]
    public async Task ProviderOutageUsesBoundedRetryAndFailsDurablyAfterTheLastAttempt()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alertId = await SeedConfirmedAlertAsync(db, NotificationChannel.Voice);
        var clock = new MutableTimeProvider(Now);
        var store = new SimulationDispatchScenarioStore(db);
        await store.SetAsync(
            DemoDataSeeder.OrganizationId,
            NotificationChannel.Voice,
            SimulationDispatchScenario.ProviderOutage,
            DemoDataSeeder.MorganUserId,
            Now,
            CancellationToken.None);
        var processor = CreateProcessor(db, clock, maxAttempts: 2);

        var first = await processor.ProcessNextAsync("worker-a", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = await processor.ProcessNextAsync("worker-b", CancellationToken.None);

        first.Rescheduled.Should().BeTrue();
        second.PermanentlyFailed.Should().BeTrue();
        (await db.DeliveryAttempts.Where(attempt => attempt.AlertId == alertId).OrderBy(attempt => attempt.AttemptNumber).ToArrayAsync())
            .Should().HaveCount(2)
            .And.OnlyContain(attempt => attempt.Status == DeliveryAttemptStatus.Failed)
            .And.Subject.Select(attempt => attempt.IdempotencyKey).Should().OnlyHaveUniqueItems();
        (await db.OutboxMessages.SingleAsync(message => message.AggregateId == alertId.Value)).ProcessingState
            .Should().Be(OutboxProcessingState.Failed);
        (await db.Alerts.SingleAsync(alert => alert.Id == alertId)).State.Should().Be(AlertState.Failed);
    }

    [Fact]
    public async Task OutOfOrderProviderEventsDoNotRegressTheDurableAttempt()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alertId = await SeedConfirmedAlertAsync(db, NotificationChannel.SecureMessage);
        var store = new SimulationDispatchScenarioStore(db);
        await store.SetAsync(
            DemoDataSeeder.OrganizationId,
            NotificationChannel.SecureMessage,
            SimulationDispatchScenario.OutOfOrderCallback,
            DemoDataSeeder.MorganUserId,
            Now,
            CancellationToken.None);
        var processor = CreateProcessor(db, new MutableTimeProvider(Now));

        await processor.ProcessNextAsync("worker-a", CancellationToken.None);

        var attempt = await db.DeliveryAttempts.SingleAsync(item => item.AlertId == alertId);
        attempt.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        attempt.SubmittedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task InvalidOutboxPayloadFailsWithoutCreatingDeliveryAttempts()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var outbox = OutboxMessage.Create(
            OutboxMessageId.New(),
            DemoDataSeeder.OrganizationId,
            "AlertDispatchRequested",
            Guid.NewGuid(),
            "{\"alertId\":\"not-a-guid\",\"draftVersion\":1,\"approvedMessage\":\"SIMULATION: forbidden\"}",
            "invalid-dispatch-payload",
            Now);
        db.OutboxMessages.Add(outbox);
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new MutableTimeProvider(Now));

        var result = await processor.ProcessNextAsync("worker-a", CancellationToken.None);

        result.PermanentlyFailed.Should().BeTrue();
        (await db.OutboxMessages.SingleAsync(message => message.Id == outbox.Id)).ProcessingState
            .Should().Be(OutboxProcessingState.Failed);
        (await db.DeliveryAttempts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnExpiredWorkerLeaseIsRecoveredByTheNextWorker()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var alertId = await SeedConfirmedAlertAsync(db, NotificationChannel.Sms);
        var outbox = await db.OutboxMessages.SingleAsync(message => message.AggregateId == alertId.Value);
        outbox.TryAcquireLease("dead-worker", Now, Now.AddMinutes(1)).Should().BeTrue();
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new MutableTimeProvider(Now.AddMinutes(2)));

        var result = await processor.ProcessNextAsync("restarted-worker", CancellationToken.None);

        result.Processed.Should().BeTrue();
        (await db.OutboxMessages.SingleAsync(message => message.Id == outbox.Id)).ProcessingState
            .Should().Be(OutboxProcessingState.Processed);
    }

    private static OutboxDispatchProcessor CreateProcessor(
        CriticalAlertsDbContext db,
        MutableTimeProvider clock,
        int maxAttempts = 2)
        => new(
            db,
            [
                new SimulationSecureMessageChannel(clock),
                new SimulationSmsChannel(clock),
                new SimulationVoiceChannel(clock),
            ],
            new SimulationDeliveryEventNormalizer(),
            new SimulationDispatchScenarioStore(db),
            clock,
            Options.Create(new DispatchWorkerOptions
            {
                LeaseDuration = TimeSpan.FromMinutes(1),
                MaxAttempts = maxAttempts,
                RetryDelay = TimeSpan.FromSeconds(1),
            }),
            NullLogger<OutboxDispatchProcessor>.Instance);

    private static async Task<AlertId> SeedConfirmedAlertAsync(
        CriticalAlertsDbContext db,
        NotificationChannel channel)
    {
        var patientReference = $"SIM-PAT-{Guid.NewGuid():N}"[..18];
        var alert = Alert.CreateDraft(
            AlertId.New(),
            DemoDataSeeder.OrganizationId,
            DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId,
            DemoDataSeeder.JordanUserId,
            patientReference,
            ProtectPatient(patientReference),
            "North Wing / Simulation Room 204",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: fictional dispatch source."),
            Now,
            Protect("{\"situation\":\"SIMULATION: fictional situation\"}"));
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();

        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.SetApprovedMessage(Protect("SIMULATION: fictional approved message."), alert.DraftVersion, Now);
        var endpointKind = channel switch
        {
            NotificationChannel.SecureMessage => ContactEndpointKind.SecureMessage,
            NotificationChannel.Sms => ContactEndpointKind.Sms,
            NotificationChannel.Voice => ContactEndpointKind.Voice,
            _ => throw new InvalidOperationException("Unsupported test channel."),
        };
        var endpoint = await db.ContactEndpoints
            .AsNoTracking()
            .Where(item => item.OrganizationId == DemoDataSeeder.OrganizationId && item.Kind == endpointKind)
            .FirstAsync();
        var practitioner = await db.Practitioners.SingleAsync(item => item.Id == endpoint.PractitionerId);
        alert.ReplaceRecipients(
            [new ValidatedRecipientSelection(
                practitioner.Id,
                null,
                channel,
                "SIM-DISPATCH-REVISION",
                Now,
                "On-call status not used")],
            DemoDataSeeder.JordanUserId,
            alert.DraftVersion,
            Now);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", DemoDataSeeder.JordanUserId, alert.DraftVersion, Now);
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, Now);
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion, [practitioner], Now, "test-dispatch");
        db.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageId.New(),
            alert.OrganizationId,
            "AlertDispatchRequested",
            alert.Id.Value,
            $"{{\"alertId\":\"{alert.Id.Value:D}\",\"draftVersion\":{alert.DraftVersion.Value}}}",
            $"alert-dispatch:{alert.Id.Value:N}:v{alert.DraftVersion.Value}",
            Now));
        await db.SaveChangesAsync();
        return alert.Id;
    }

    private static ProtectedValue Protect(string value)
        => new(Encoding.UTF8.GetBytes(value), "test-v1", "dispatch-test");

    private static ProtectedValue ProtectPatient(string value)
        => new(Encoding.UTF8.GetBytes(value), "test-v1", ProtectedValuePurposes.AlertPatientReference);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
