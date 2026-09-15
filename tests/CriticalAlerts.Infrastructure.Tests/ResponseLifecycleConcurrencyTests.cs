using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Responses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class ResponseLifecycleConcurrencyTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T12:00:00Z");

    [Fact]
    public async Task LifecycleRejectsBackwardEvidenceButSuccessfulReplaySurvivesLaterFutureEvidence()
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(true);
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alerts SET updated_at_utc = clock_timestamp() + interval '1 hour' WHERE id = {alert.Id.Value}");
        var service = new AlertLifecycleService(db);
        var request = new AlertLifecycleActionRequest(alert.DraftVersion.Value);
        var action = () => service.ResolveAsync(alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-clock", alert.Id, request, "SIM-clock", default);
        await action.Should().ThrowAsync<AlertLifecycleValidationException>().Where(x => x.Code == "lifecycle-conflict");
        await using (var verify = fixture.CreateContext())
        {
            (await verify.Alerts.SingleAsync()).State.Should().Be(AlertState.Active);
            (await verify.IdempotencyRecords.CountAsync()).Should().Be(0);
        }
        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alerts SET updated_at_utc = clock_timestamp() - interval '1 minute' WHERE id = {alert.Id.Value}");
        (await action())!.State.Should().Be("Resolved");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alerts SET updated_at_utc = clock_timestamp() + interval '1 hour' WHERE id = {alert.Id.Value}");
        (await action())!.Replayed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitingRecipientMutationUsesTimeAfterCommittedSelection(bool opened)
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(false);
        await using var activationDb = fixture.CreateContext();
        await using var transaction = await activationDb.Database.BeginTransactionAsync();
        await activationDb.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM alerts WHERE id = {alert.Id.Value} FOR UPDATE");
        await using var responseDb = fixture.CreateContext();
        await responseDb.Database.OpenConnectionAsync();
        var pid = ((NpgsqlConnection)responseDb.Database.GetDbConnection()).ProcessID;
        var service = new RecipientResponseService(responseDb, new PractitionerIdentityResolver(responseDb));
        Task action = opened
            ? service.MarkOpenedAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-order", alert.Id,
                new OpenRecipientAlertRequest(alert.DraftVersion.Value), "SIM-order-open", default)
            : service.RecordAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-order", alert.Id,
                new RecordRecipientResponseRequest(alert.DraftVersion.Value, "Declined"), "SIM-order-response", default);
        await WaitForCompletionOrLockAsync(action, pid);
        await activationDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_recipient_selections SET selected_at_utc = clock_timestamp() WHERE alert_id = {alert.Id.Value}");
        var selectedAt = await activationDb.AlertRecipientSelections.Where(x => x.AlertId == alert.Id).Select(x => x.SelectedAtUtc).SingleAsync();
        await transaction.CommitAsync();
        await action;
        await using var verify = fixture.CreateContext();
        var occurredAt = opened ? (await verify.DeliveryAttempts.SingleAsync()).OpenedAtUtc!.Value
            : (await verify.RecipientResponses.SingleAsync()).OccurredAtUtc;
        occurredAt.Should().BeOnOrAfter(selectedAt, "the selection transaction won the shared lock before the recipient mutation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseTimeBeforeSelectionReturnsSafeConflictWithoutMutations(bool opened)
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(false);
        await using var db = fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_recipient_selections SET selected_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {alert.Id.Value}");
        var service = new RecipientResponseService(db, new PractitionerIdentityResolver(db));
        Func<Task> action = () => opened
            ? service.MarkOpenedAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-clock", alert.Id,
                new OpenRecipientAlertRequest(alert.DraftVersion.Value), "SIM-clock-open", default)
            : service.RecordAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-clock", alert.Id,
                new RecordRecipientResponseRequest(alert.DraftVersion.Value, "Accepted"), "SIM-clock-response", default);
        await action.Should().ThrowAsync<RecipientResponseValidationException>().Where(x => x.Code == "response-conflict");
        await using var verify = fixture.CreateContext();
        (await verify.RecipientResponses.CountAsync()).Should().Be(0);
        (await verify.ResponsibilityAssignments.CountAsync()).Should().Be(0);
        (await verify.IdempotencyRecords.CountAsync()).Should().Be(0);
        (await verify.DeliveryAttempts.SingleAsync()).OpenedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedRecipientReplaySurvivesLaterFutureSelection(bool opened)
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(false);
        await using var db = fixture.CreateContext();
        var service = new RecipientResponseService(db, new PractitionerIdentityResolver(db));
        async Task<bool> Replay()
            => opened
                ? (await service.MarkOpenedAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-replay", alert.Id,
                    new OpenRecipientAlertRequest(alert.DraftVersion.Value), "SIM-replay-open", default))!.Replayed
                : (await service.RecordAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-replay", alert.Id,
                    new RecordRecipientResponseRequest(alert.DraftVersion.Value, "Accepted"), "SIM-replay-response", default))!.Replayed;
        (await Replay()).Should().BeFalse();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE alert_recipient_selections SET selected_at_utc = clock_timestamp() + interval '1 hour' WHERE alert_id = {alert.Id.Value}");
        (await Replay()).Should().BeTrue();
        await using var verify = fixture.CreateContext();
        (await verify.IdempotencyRecords.CountAsync()).Should().Be(1);
        (await verify.RecipientResponses.CountAsync()).Should().Be(opened ? 0 : 1);
        (await verify.ResponsibilityAssignments.CountAsync()).Should().Be(opened ? 0 : 1);
    }

    [Fact]
    public async Task ResolutionWaitsForConcurrentAcceptanceBeforeCheckingResponsibility()
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(accepted: false);
        var gate = new PauseAfterSave();
        await using var responseDb = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(gate).Options);
        await using var lifecycleDb = fixture.CreateContext();
        await lifecycleDb.Database.OpenConnectionAsync();
        var lifecyclePid = ((NpgsqlConnection)lifecycleDb.Database.GetDbConnection()).ProcessID;
        var response = new RecipientResponseService(responseDb, new PractitionerIdentityResolver(responseDb));
        var responseTask = response.RecordAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-race", alert.Id,
            new RecordRecipientResponseRequest(alert.DraftVersion.Value, "Accepted"), "SIM-accept-race", default);
        await gate.Saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var lifecycleTask = new AlertLifecycleService(lifecycleDb).ResolveAsync(
            alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-race", alert.Id,
            new AlertLifecycleActionRequest(alert.DraftVersion.Value), "SIM-resolve-race", default);
        try
        {
            await WaitForCompletionOrLockAsync(lifecycleTask, lifecyclePid);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        (await responseTask).Should().NotBeNull();
        (await lifecycleTask)!.State.Should().Be("Resolved");
        await using var verify = fixture.CreateContext();
        (await verify.ResponsibilityAssignments.CountAsync()).Should().Be(1);
        (await verify.Alerts.SingleAsync()).State.Should().Be(AlertState.Resolved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TerminalLifecycleCommitPreventsConcurrentRecipientMutation(bool resolve, bool opened)
    {
        await fixture.ResetAsync();
        var alert = await CreateActiveAlertAsync(resolve);
        var gate = new PauseAfterSave();
        await using var lifecycleDb = new CriticalAlertsDbContext(new DbContextOptionsBuilder<CriticalAlertsDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(gate).Options);
        await using var responseDb = fixture.CreateContext();
        await responseDb.Database.OpenConnectionAsync();
        var responsePid = ((NpgsqlConnection)responseDb.Database.GetDbConnection()).ProcessID;
        var lifecycle = new AlertLifecycleService(lifecycleDb);
        var response = new RecipientResponseService(responseDb, new PractitionerIdentityResolver(responseDb));
        var lifecycleTask = resolve
            ? lifecycle.ResolveAsync(alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-race", alert.Id,
                new AlertLifecycleActionRequest(alert.DraftVersion.Value), "SIM-resolve-race", default)
            : lifecycle.CancelAsync(alert.OrganizationId, DemoDataSeeder.JordanUserId, "SIM-race", alert.Id,
                new AlertLifecycleActionRequest(alert.DraftVersion.Value), "SIM-cancel-race", default);
        await gate.Saved.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<object?> responseTask = RespondAsync();
        try
        {
            // Wait for either the old unsafe commit or a real PostgreSQL lock wait; no timing guess.
            await WaitForCompletionOrLockAsync(responseTask, responsePid);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        (await lifecycleTask).Should().NotBeNull();
        (await responseTask).Should().BeNull("a recipient command must recheck state after the lifecycle transaction commits");
        await using var verify = fixture.CreateContext();
        (await verify.RecipientResponses.CountAsync()).Should().Be(resolve ? 1 : 0);
        (await verify.ResponsibilityAssignments.CountAsync()).Should().Be(resolve ? 1 : 0);
        (await verify.DeliveryAttempts.SingleAsync()).OpenedAtUtc.Should().BeNull();
        (await verify.AuditEvents.CountAsync(item => item.Action.StartsWith("recipient."))).Should().Be(0);

        async Task<object?> RespondAsync() => opened
            ? await response.MarkOpenedAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-race", alert.Id,
                new OpenRecipientAlertRequest(alert.DraftVersion.Value), "SIM-open-race", default)
            : await response.RecordAsync(alert.OrganizationId, DemoDataSeeder.RileyUserId, "SIM-race", alert.Id,
                new RecordRecipientResponseRequest(alert.DraftVersion.Value, resolve ? "Acknowledged" : "Accepted"),
                "SIM-response-race", default);
    }

    private async Task WaitForCompletionOrLockAsync(Task task, int pid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var monitor = new NpgsqlConnection(fixture.ConnectionString);
        await monitor.OpenAsync(timeout.Token);
        while (!task.IsCompleted)
        {
            await using var command = new NpgsqlCommand(
                "SELECT wait_event_type = 'Lock' FROM pg_stat_activity WHERE pid = @pid", monitor);
            command.Parameters.AddWithValue("pid", pid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true)
            {
                return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private async Task<Alert> CreateActiveAlertAsync(bool accepted)
    {
        await using var db = fixture.CreateContext();
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId,
            DemoDataSeeder.EmergencyDepartmentId, DemoDataSeeder.JordanUserId, "SIM-PAT-RACE",
            Protect("SIM-PAT-RACE", ProtectedValuePurposes.AlertPatientReference), "SIMULATION room", "DEMO-URGENT",
            AlertSourceType.Typed, Protect("SIMULATION: fictional race source", ProtectedValuePurposes.AlertTypedSource), Now,
            Protect("{\"situation\":\"SIMULATION: fictional situation\"}", ProtectedValuePurposes.AlertSbar));
        alert.SetApprovedMessage(Protect("SIMULATION: fictional approved message", ProtectedValuePurposes.AlertApprovedMessage),
            alert.DraftVersion, Now.AddMinutes(1));
        alert.ReplaceRecipients([new ValidatedRecipientSelection(DemoDataSeeder.RileySatoId, null,
            NotificationChannel.SecureMessage, "SIM-REV-RACE", Now, "Primary")], DemoDataSeeder.JordanUserId,
            alert.DraftVersion, Now.AddMinutes(2));
        alert.SubmitForConfirmation(DemoDataSeeder.JordanUserId, alert.DraftVersion, Now.AddMinutes(3));
        alert.ConfirmForDispatch(DemoDataSeeder.JordanUserId, alert.DraftVersion,
            [await db.Practitioners.SingleAsync(item => item.Id == DemoDataSeeder.RileySatoId)], Now.AddMinutes(4), "SIM-confirm-race");
        alert.MarkActive(Now.AddMinutes(5), "SIM-active-race");
        db.Alerts.Add(alert);
        db.DeliveryAttempts.Add(DeliveryAttempt.CreateRequested(DeliveryAttemptId.New(), alert.OrganizationId, alert.Id,
            alert.RecipientSelections.Single().Id, NotificationChannel.SecureMessage, 1, "SIM-delivery-race", "simulation", Now.AddMinutes(5)));
        if (accepted)
        {
            var response = RecipientResponse.Record(RecipientResponseId.New(), alert.OrganizationId, alert.Id,
                alert.DraftVersion, DemoDataSeeder.RileySatoId, RecipientResponseType.Accepted, DemoDataSeeder.RileyUserId,
                Now.AddMinutes(6), "simulation-responsibility-accepted");
            db.RecipientResponses.Add(response);
            db.ResponsibilityAssignments.Add(ResponsibilityAssignment.FromResponse(response)!);
        }

        await db.SaveChangesAsync();
        return alert;
    }

    private static ProtectedValue Protect(string value, string purpose)
        => new(System.Text.Encoding.UTF8.GetBytes(value), "test-v1", purpose);

    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Saved.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
}
