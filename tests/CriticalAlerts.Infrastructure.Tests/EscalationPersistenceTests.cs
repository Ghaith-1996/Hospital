using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

[Collection(MigratedPostgresCollection.Name)]
public sealed class EscalationPersistenceTests(MigratedPostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-07T12:00:00Z");

    [Fact]
    public async Task BoundRunAndConsumedSignalsSurviveReloadAndRejectReplay()
    {
        await using var db = fixture.CreateContext();
        var (plan, run, selection, response) = await CreateRun(db);
        run.AcquireLease("demo-worker", Now, TimeSpan.FromMinutes(5));
        run.ConsumeSignal(response, selection, "demo-worker", Now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.EscalationRuns.SingleAsync(r => r.Id == run.Id);
        loaded.PlanId.Should().Be(plan.Id);
        loaded.AlertVersion.Should().Be(new AlertDraftVersion(1));
        loaded.ConsumedSignals.Should().ContainSingle();
        var replay = () => loaded.ConsumeSignal(response, selection, "demo-worker", Now);
        replay.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task OnlyOneBoundRunMayExistForExactAlertVersion()
    {
        await using var db = fixture.CreateContext();
        var (plan, _, _, _) = await CreateRun(db);
        db.EscalationRuns.Add(EscalationRun.Schedule(EscalationRunId.New(), plan, new(1), Now));
        var save = () => db.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Theory]
    [InlineData("UPDATE escalation_runs SET alert_version = 2 WHERE id = @id")]
    [InlineData("UPDATE escalation_runs SET plan_revision = 'wrong' WHERE id = @id")]
    [InlineData("UPDATE escalation_runs SET policy_version = 'wrong' WHERE id = @id")]
    public async Task DatabaseRejectsMismatchedExactPlan(string sql)
    {
        await using var db = fixture.CreateContext();
        var (_, run, _, _) = await CreateRun(db);
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", run.Id.Value);
        var write = () => command.ExecuteNonQueryAsync();
        (await write.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Theory]
    [InlineData("UPDATE escalation_events SET step_sequence = step_sequence WHERE run_id = @id")]
    [InlineData("DELETE FROM escalation_events WHERE run_id = @id")]
    [InlineData("TRUNCATE escalation_events")]
    [InlineData("UPDATE escalation_consumed_signals SET step_sequence = step_sequence WHERE run_id = @id")]
    [InlineData("DELETE FROM escalation_consumed_signals WHERE run_id = @id")]
    [InlineData("TRUNCATE escalation_consumed_signals")]
    public async Task DatabaseHistoryIsAppendOnly(string sql)
    {
        await using var db = fixture.CreateContext();
        var (_, run, selection, response) = await CreateRun(db);
        run.AcquireLease("demo-worker", Now, TimeSpan.FromMinutes(5));
        run.ConsumeSignal(response, selection, "demo-worker", Now);
        db.Set<EscalationEvent>().Add(EscalationEvent.Record(run, EscalationEventType.Scheduled, 1, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", run.Id.Value);
        var write = () => command.ExecuteNonQueryAsync();
        (await write.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
    [Fact]
    public async Task SignalCannotUseAnotherPractitionersSelection()
    {
        await using var db = fixture.CreateContext();
        var (_, run, selection, response) = await CreateRun(db);
        var otherPractitioner = await db.Practitioners.FirstAsync(p => p.OrganizationId == run.OrganizationId && p.Id != selection.PractitionerId);
        var otherSelection = new AlertRecipientSelection(AlertRecipientSelectionId.New(), run.OrganizationId, run.AlertId, new(1), otherPractitioner.Id, null, NotificationChannel.SecureMessage, DemoDataSeeder.JordanUserId, Now, "DEMO-revision", Now, null);
        db.AlertRecipientSelections.Add(otherSelection);
        await db.SaveChangesAsync();
        var insert = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO escalation_consumed_signals (id, organization_id, run_id, alert_id, alert_version, response_id, recipient_selection_id, step_sequence, consumed_at_utc)
            VALUES ({Guid.NewGuid()}, {run.OrganizationId.Value}, {run.Id.Value}, {run.AlertId.Value}, 1, {response.Id.Value}, {otherSelection.Id.Value}, 1, {Now})
            """);
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task PauseRemainingDelayAndLeaseSurviveSeparateContexts()
    {
        EscalationRunId id;
        await using (var db = fixture.CreateContext())
        {
            var (_, run, _, _) = await CreateRun(db);
            run.Pause(DemoDataSeeder.JordanUserId, Now.AddSeconds(15));
            id = run.Id;
            await db.SaveChangesAsync();
        }
        await using (var db = fixture.CreateContext())
        {
            var run = await db.EscalationRuns.SingleAsync(r => r.Id == id);
            run.RemainingDelay.Should().Be(TimeSpan.FromSeconds(45));
            run.Resume(DemoDataSeeder.JordanUserId, Now.AddMinutes(5));
            run.NextDueAtUtc.Should().Be(Now.AddMinutes(5).AddSeconds(45));
            run.AcquireLease("demo-restart", Now.AddMinutes(5), TimeSpan.FromMinutes(1));
            await db.SaveChangesAsync();
        }
        await using var verify = fixture.CreateContext();
        var loaded = await verify.EscalationRuns.SingleAsync(r => r.Id == id);
        loaded.LeaseOwner.Should().Be("demo-restart");
        loaded.LeaseExpiresAtUtc.Should().Be(Now.AddMinutes(6));
        loaded.PausedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task DatabaseRejectsSignalReplayWithFreshIdentity()
    {
        await using var db = fixture.CreateContext();
        var (_, run, selection, response) = await CreateRun(db);
        run.AcquireLease("demo-worker", Now, TimeSpan.FromMinutes(5));
        run.ConsumeSignal(response, selection, "demo-worker", Now);
        await db.SaveChangesAsync();
        var insert = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO escalation_consumed_signals (id, organization_id, run_id, alert_id, alert_version, response_id, recipient_selection_id, step_sequence, consumed_at_utc)
            SELECT {Guid.NewGuid()}, organization_id, run_id, alert_id, alert_version, response_id, recipient_selection_id, step_sequence, consumed_at_utc FROM escalation_consumed_signals WHERE run_id = {run.Id.Value}
            """);
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task DatabaseRejectsSignalFromAnotherAlertAndVersion()
    {
        await using var db = fixture.CreateContext();
        var (_, run, selection, _) = await CreateRun(db);
        var (_, _, _, foreignResponse) = await CreateRun(db);
        var insert = () => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO escalation_consumed_signals (id, organization_id, run_id, alert_id, alert_version, response_id, recipient_selection_id, step_sequence, consumed_at_utc)
            VALUES ({Guid.NewGuid()}, {run.OrganizationId.Value}, {run.Id.Value}, {run.AlertId.Value}, 1, {foreignResponse.Id.Value}, {selection.Id.Value}, 1, {Now})
            """);
        (await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
    }
    [Fact]
    public async Task DatabaseRejectsPartiallyClearedBinding()
    {
        await using var db = fixture.CreateContext();
        var (_, run, _, _) = await CreateRun(db);
        var update = () => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE escalation_runs SET alert_version = NULL WHERE id = {run.Id.Value}");
        (await update.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task LogicalStepEventIsUniqueButRepeatedHumanPauseIsAllowed()
    {
        await using var db = fixture.CreateContext();
        var (_, run, _, _) = await CreateRun(db);
        db.Set<EscalationEvent>().AddRange(
            EscalationEvent.Record(run, EscalationEventType.Paused, 1, DemoDataSeeder.JordanUserId, Guid.NewGuid(), Now, overrideReason: EscalationOverrideReason.OperatorReview),
            EscalationEvent.Record(run, EscalationEventType.Paused, 1, DemoDataSeeder.JordanUserId, Guid.NewGuid(), Now.AddSeconds(1), overrideReason: EscalationOverrideReason.ManualCoordination),
            EscalationEvent.Record(run, EscalationEventType.StepDue, 1, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();
        db.Set<EscalationEvent>().Add(EscalationEvent.Record(run, EscalationEventType.StepDue, 1, null, Guid.NewGuid(), Now));
        var save = () => db.SaveChangesAsync();
        (await save.Should().ThrowAsync<DbUpdateException>()).Which.InnerException.Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
    private async Task<(AlertEscalationPlan Plan, EscalationRun Run, AlertRecipientSelection Selection, RecipientResponse Response)> CreateRun(CriticalAlertsDbContext db)
    {
        var protector = AesGcmSensitiveDataProtector.FromBase64(fixture.DataProtectionKey);
        var protectedReference = protector.Protect("SIM-PAT-ESCALATION", new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, DemoDataSeeder.OrganizationId.Value));
        var source = protector.Protect("SIMULATION persistence", new SensitiveDataContext(ProtectedValuePurposes.AlertTypedSource, DemoDataSeeder.OrganizationId.Value));
        var alert = Alert.CreateDraft(AlertId.New(), DemoDataSeeder.OrganizationId, DemoDataSeeder.NorthSiteId, DemoDataSeeder.EmergencyDepartmentId, DemoDataSeeder.JordanUserId, "SIM-PAT-ESCALATION", protectedReference, "SIMULATION room", "DEMO-URGENT", AlertSourceType.Typed, source, Now);
        db.Alerts.Add(alert);
        await db.SaveChangesAsync();
        var policy = await db.EscalationPolicies.FirstAsync(p => p.OrganizationId == DemoDataSeeder.OrganizationId);
        var role = await db.PractitionerRoles.FirstAsync(p => p.OrganizationId == DemoDataSeeder.OrganizationId);
        var definition = new EscalationPlanDefinition(DemoDataSeeder.OrganizationId.Value, alert.Id.Value, 1, policy.Id.Value, policy.Version, "DEMO trigger", "DEMO stop", "DEMO primary", [new(1, 60, 1, "DEMO backup", [new(role.PractitionerId.Value, role.Id.Value, "Fictional DEMO", "DEMO role", "SecureMessage", "DEMO-revision", Now, "No assignment")])]);
        var plan = AlertEscalationPlan.Confirm(definition, AlertEscalationPlan.ComputeRevision(definition), DemoDataSeeder.JordanUserId, Now);
        db.AlertEscalationPlans.Add(plan);
        await db.SaveChangesAsync();
        var run = EscalationRun.Schedule(EscalationRunId.New(), plan, new(1), Now);
        var selection = new AlertRecipientSelection(AlertRecipientSelectionId.New(), plan.OrganizationId, alert.Id, new(1), role.PractitionerId, role.Id, NotificationChannel.SecureMessage, DemoDataSeeder.JordanUserId, Now, "DEMO-revision", Now, null);
        var response = RecipientResponse.Record(RecipientResponseId.New(), plan.OrganizationId, alert.Id, new(1), role.PractitionerId, RecipientResponseType.Declined, DemoDataSeeder.JordanUserId, Now, RecipientResponse.DefaultReasonCode(RecipientResponseType.Declined));
        db.AlertRecipientSelections.Add(selection);
        db.RecipientResponses.Add(response);
        db.EscalationRuns.Add(run);
        await db.SaveChangesAsync();
        return (plan, run, selection, response);
    }
}
