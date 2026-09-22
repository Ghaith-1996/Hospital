using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationProcessorTests(MigratedPostgresFixture fixture)
{
    [Fact]
    public async Task EscalationOutboxDispatchesOnlyNewSelectionAndOriginalOutboxNeverRedispatchesIt()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        await EscalationPersistenceTests.SeedApprovalAsync(db, 0);
        await new EscalationProcessor(db).ProcessNextAsync("escalation-worker");
        var dispatch = OutboxDispatchProcessorTests.CreateProcessor(db, TimeProvider.System);
        (await dispatch.ProcessNextAsync("dispatch-worker", default)).Processed.Should().BeTrue();
        var selections = await db.AlertRecipientSelections.ToArrayAsync();
        var manual = selections.Single(row => row.SelectionSource == RecipientSelectionSource.Manual);
        (await db.DeliveryAttempts.ToArrayAsync()).Should().ContainSingle().Which.RecipientSelectionId.Should().Be(manual.Id);
        (await dispatch.ProcessNextAsync("dispatch-worker", default)).Processed.Should().BeTrue();
        (await db.DeliveryAttempts.ToArrayAsync()).Should().HaveCount(2).And.OnlyContain(row => row.Status == DeliveryAttemptStatus.Delivered);
        (await dispatch.ProcessNextAsync("dispatch-worker", default)).Processed.Should().BeFalse();
    }

    [Theory]
    [InlineData(RecipientResponseType.Acknowledged, false)]
    [InlineData(RecipientResponseType.Accepted, true)]
    [InlineData(RecipientResponseType.Declined, false)]
    [InlineData(RecipientResponseType.Unavailable, false)]
    public async Task OnlyResponsibilityStopsWhileNegativeResponsesExpedite(RecipientResponseType responseType, bool stops)
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db, responseType == RecipientResponseType.Acknowledged ? 0 : 3600);
        var reason = responseType switch { RecipientResponseType.Accepted => "simulation-responsibility-accepted",
            RecipientResponseType.Declined => "simulation-declined", RecipientResponseType.Unavailable => "simulation-unavailable", _ => "simulation-acknowledged" };
        var response = RecipientResponse.Record(RecipientResponseId.New(), approval.OrganizationId, approval.AlertId,
            approval.AlertVersion, DemoDataSeeder.MayaChenId, responseType, DemoDataSeeder.JordanUserId, DateTimeOffset.UtcNow, reason);
        db.RecipientResponses.Add(response);
        if (ResponsibilityAssignment.FromResponse(response) is { } assignment) db.ResponsibilityAssignments.Add(assignment);
        await db.SaveChangesAsync();
        await new EscalationProcessor(db).ProcessNextAsync("worker");
        (await db.EscalationRuns.SingleAsync()).State.Should().Be(stops ? EscalationRunState.Stopped : EscalationRunState.Exhausted);
        (await db.AlertRecipientSelections.CountAsync(row => row.SelectionSource == RecipientSelectionSource.EscalationPolicy)).Should().Be(stops ? 0 : 1);
    }

    [Fact]
    public async Task ConcurrentWorkersScheduleOnceFromApprovedVersionUsingDatabaseClock()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db);
        var before = await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync();
        await using var other = fixture.CreateContext();
        await Task.WhenAll(new EscalationProcessor(db).ProcessNextAsync("worker-a"),
            new EscalationProcessor(other).ProcessNextAsync("worker-b"));
        db.ChangeTracker.Clear();
        var run = await db.EscalationRuns.SingleAsync();
        run.AlertVersion.Should().Be(approval.AlertVersion);
        run.StartedAtUtc.Should().BeOnOrAfter(before);
        run.NextDueAtUtc.Should().Be(run.StartedAtUtc.AddSeconds(60));
        (await db.EscalationEvents.CountAsync(row => row.Kind == EscalationEventKind.Scheduled)).Should().Be(1);
    }

    [Fact]
    public async Task LegacyConfirmedAlertDoesNotAcquireAnInferredPlan()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var id = await OutboxDispatchProcessorTests.SeedConfirmedAlertAsync(db, NotificationChannel.SecureMessage);
        (await db.Alerts.SingleAsync(row => row.Id == id)).MarkActive(DateTimeOffset.UtcNow, "legacy-test");
        await db.SaveChangesAsync();
        (await new EscalationProcessor(db).ProcessNextAsync("worker")).Should().BeFalse();
        (await db.EscalationRuns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExpiredLeaseRecoversAndDueStepActivatesOnlyApprovedBackupExactlyOnce()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var approval = await EscalationPersistenceTests.SeedApprovalAsync(db, 0);
        var now = DateTimeOffset.UtcNow.AddMinutes(-2);
        var run = EscalationRun.Schedule(EscalationRunId.New(), approval.OrganizationId, approval.AlertId,
            approval.PolicyId, approval.PolicyVersion, now, now, approval.AlertVersion);
        run.TryAcquireLease("dead-worker", now, TimeSpan.FromSeconds(1));
        db.EscalationRuns.Add(run);
        db.EscalationEvents.Add(run.Record(EscalationEventKind.Scheduled, now));
        await db.SaveChangesAsync();
        await using var other = fixture.CreateContext();
        await Task.WhenAll(new EscalationProcessor(db).ProcessNextAsync("worker-a"),
            new EscalationProcessor(other).ProcessNextAsync("worker-b"));
        db.ChangeTracker.Clear();
        var selection = await db.AlertRecipientSelections.SingleAsync(row => row.SelectionSource == RecipientSelectionSource.EscalationPolicy);
        selection.PractitionerId.Should().Be(DemoDataSeeder.RileySatoId);
        selection.AlertVersion.Should().Be(approval.AlertVersion);
        (await db.Alerts.SingleAsync(row => row.Id == approval.AlertId)).DraftVersion.Should().Be(approval.AlertVersion);
        var message = await db.OutboxMessages.SingleAsync(row => row.EventType == "EscalationDispatchRequested");
        message.PayloadJson.Should().Contain(selection.Id.Value.ToString()).And.NotContain("Riley").And.NotContain("SIMULATION:");
        (await db.EscalationRuns.SingleAsync()).State.Should().Be(EscalationRunState.Exhausted);
        (await db.EscalationEvents.CountAsync(row => row.Kind == EscalationEventKind.RecipientActivated)).Should().Be(1);
    }
}
