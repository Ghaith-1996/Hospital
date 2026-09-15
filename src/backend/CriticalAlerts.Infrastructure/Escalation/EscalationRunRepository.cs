using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed record EscalationClaim(OrganizationId OrganizationId, AlertId AlertId, EscalationRunId RunId, string LeaseOwner);
public sealed record LockedEscalationRun(Alert Alert, EscalationRun Run, AlertEscalationPlan Plan, DateTimeOffset Now);

public sealed class EscalationRunRepository(CriticalAlertsDbContext db)
{
    public async Task<IReadOnlyList<EscalationRunId>> FindCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT r.id AS "Value" FROM escalation_runs r
            JOIN alerts a ON a.id = r.alert_id AND a.organization_id = r.organization_id
            JOIN alert_escalation_plans p ON p.id = r.plan_id AND p.organization_id = r.organization_id
            WHERE r.alert_version IS NOT NULL AND r.plan_id IS NOT NULL AND r.plan_revision IS NOT NULL
              AND a.draft_version = r.alert_version AND a.confirmed_draft_version = r.alert_version
              AND a.exact_escalation_plan_id = r.plan_id AND a.exact_escalation_plan_revision = r.plan_revision
              AND a.exact_escalation_policy_id = r.policy_id AND a.exact_escalation_policy_version = r.policy_version
              AND p.definition_json::jsonb ->> 'TriggerCondition' = {DemoEscalationSemantics.TriggerCondition}
              AND p.definition_json::jsonb ->> 'StopCondition' = {DemoEscalationSemantics.StopCondition}
              AND r.state IN ('Scheduled', 'Running', 'Paused')
              AND COALESCE(r.updated_at_utc, r.started_at_utc) <= clock_timestamp()
              AND (r.lease_expires_at_utc IS NULL OR r.lease_expires_at_utc <= clock_timestamp())
              AND (a.state IN ('Resolved', 'Cancelled') OR EXISTS (
                SELECT 1 FROM responsibility_assignments x WHERE x.organization_id = r.organization_id
                  AND x.alert_id = r.alert_id AND x.alert_version = r.alert_version
                  AND x.released_at_utc IS NULL AND x.accepted_at_utc <= clock_timestamp())
                OR (a.state = 'Active' AND a.draft_version = r.alert_version AND a.confirmed_draft_version = r.alert_version
                  AND a.exact_escalation_plan_id = r.plan_id AND a.exact_escalation_plan_revision = r.plan_revision
                  AND r.state <> 'Paused' AND (r.next_due_at_utc <= clock_timestamp() OR EXISTS (
                    SELECT 1 FROM recipient_responses s JOIN alert_recipient_selections v
                      ON v.organization_id = s.organization_id AND v.alert_id = s.alert_id
                      AND v.alert_version = s.alert_version AND v.practitioner_id = s.practitioner_id
                    WHERE s.organization_id = r.organization_id AND s.alert_id = r.alert_id AND s.alert_version = r.alert_version
                      AND s.response_type IN ('Declined', 'Unavailable') AND s.occurred_at_utc <= clock_timestamp()
                      AND v.selected_at_utc <= s.occurred_at_utc
                      AND NOT EXISTS (SELECT 1 FROM escalation_consumed_signals c WHERE c.run_id = r.id
                        AND (c.response_id = s.id OR c.step_sequence = r.current_step))))))
            ORDER BY r.next_due_at_utc, r.id LIMIT 100
            """).ToArrayAsync(cancellationToken);
        return ids.Select(id => new EscalationRunId(id)).ToArray();
    }

    public async Task<EscalationClaim?> ClaimNextAsync(string processOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        foreach (var id in await FindCandidatesAsync(cancellationToken))
        {
            var claim = await TryClaimAsync(id, processOwner, leaseDuration, cancellationToken);
            if (claim is not null) return claim;
        }
        return null;
    }

    public async Task<EscalationClaim?> TryClaimAsync(EscalationRunId id, string processOwner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(processOwner) || processOwner.Length > 64
            || processOwner.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            || leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
            throw new ArgumentException("A safe process owner and bounded lease duration are required.");
        var candidate = await db.EscalationRuns.AsNoTracking().Where(r => r.Id == id)
            .Select(r => new { r.OrganizationId, r.AlertId }).SingleOrDefaultAsync(cancellationToken);
        if (candidate is null) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AlertMutationLock.TryAcquireAsync(db, candidate.OrganizationId, candidate.AlertId, cancellationToken)
            || !await LockRunAsync(candidate.OrganizationId, id, skipLocked: true, cancellationToken)) return null;
        var locked = await ReloadAsync(candidate.OrganizationId, candidate.AlertId, id, cancellationToken);
        if (locked is null || !await IsCandidateAsync(locked, cancellationToken)) return null;
        // Unique per acquisition, including retries by the same process. Never accept a process prefix as proof of ownership.
        var token = $"{processOwner}-{Guid.NewGuid():N}";
        locked.Run.AcquireLease(token, locked.Now, leaseDuration);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(candidate.OrganizationId, candidate.AlertId, id, token);
    }

    // Callback mutations and any events/audit/outbox added to this SAME scoped DbContext
    // are committed atomically. Exceptions roll back everything; discard the scope after failure.
    // Every successful callback consumes its acquisition, including nonterminal Advance.
    // The callback must check latest stop/pause/Active facts before effects; this method never activates a recipient.
    public async Task<bool> ExecuteClaimAsync(EscalationClaim claim, Func<LockedEscalationRun, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AlertMutationLock.AcquireAsync(db, claim.OrganizationId, claim.AlertId, cancellationToken);
        if (!await LockRunAsync(claim.OrganizationId, claim.RunId, skipLocked: false, cancellationToken)) return false;
        var locked = await ReloadAsync(claim.OrganizationId, claim.AlertId, claim.RunId, cancellationToken);
        if (locked is null || locked.Run.LeaseOwner != claim.LeaseOwner || locked.Run.LeaseExpiresAtUtc <= locked.Now
            || locked.Run.LeaseExpiresAtUtc is null || locked.Now < (locked.Run.UpdatedAtUtc ?? locked.Run.StartedAtUtc)
            || locked.Run.State is EscalationRunState.Completed or EscalationRunState.Stopped) return false;
        var acquiredDeadline = locked.Run.LeaseExpiresAtUtc.Value;
        await action(locked, cancellationToken);
        // A callback may perform database work. Reject an expired acquisition even if it already cleared its lease in memory.
        var commitNow = await new DatabaseClock(db).GetUtcNowAsync(cancellationToken);
        if (acquiredDeadline <= commitNow) throw new DomainException("The escalation claim expired before commit.");
        if (commitNow < (locked.Run.UpdatedAtUtc ?? locked.Run.StartedAtUtc))
        {
            // A backwards wall-clock adjustment after the callback is a deferral, not a partial success.
            // Retain the persisted acquisition so a fresh scope may retry it while it is still valid.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return false;
        }
        if (locked.Run.LeaseOwner == claim.LeaseOwner) locked.Run.ReleaseLease(claim.LeaseOwner, commitNow);
        await db.SaveChangesAsync(cancellationToken);
        if (acquiredDeadline <= await new DatabaseClock(db).GetUtcNowAsync(cancellationToken))
            throw new DomainException("The escalation claim expired before commit.");
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> LockRunAsync(OrganizationId organizationId, EscalationRunId id, bool skipLocked, CancellationToken cancellationToken)
    {
        var query = skipLocked
            ? db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM escalation_runs WHERE organization_id = {organizationId.Value} AND id = {id.Value} FOR UPDATE SKIP LOCKED")
            : db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM escalation_runs WHERE organization_id = {organizationId.Value} AND id = {id.Value} FOR UPDATE");
        return (await query.ToArrayAsync(cancellationToken)).Length == 1;
    }

    private async Task<LockedEscalationRun?> ReloadAsync(OrganizationId organizationId, AlertId alertId, EscalationRunId runId, CancellationToken cancellationToken)
    {
        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == alertId, cancellationToken);
        var run = await db.EscalationRuns.SingleOrDefaultAsync(r => r.OrganizationId == organizationId && r.Id == runId && r.AlertId == alertId, cancellationToken);
        if (alert is null || run is null) return null;
        await db.Entry(alert).ReloadAsync(cancellationToken);
        await db.Entry(run).ReloadAsync(cancellationToken);
        // Signals are append-only; load the whole unfiltered collection even on a previously tracked claim.
        await db.Entry(run).Collection(r => r.ConsumedSignals).Query().LoadAsync(cancellationToken);
        var plan = await db.AlertEscalationPlans.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == run.PlanId, cancellationToken);
        if (plan is null || !Matches(alert, plan) || run.AlertVersion?.Value != plan.AlertVersion
            || run.PlanRevision != plan.Revision || run.PolicyId != plan.EscalationPolicyId || run.PolicyVersion != plan.EscalationPolicyVersion
            || !DemoEscalationSemantics.IsSupported(plan.Definition)) return null;
        return new(alert, run, plan, await new DatabaseClock(db).GetUtcNowAsync(cancellationToken));
    }

    private async Task<bool> IsCandidateAsync(LockedEscalationRun locked, CancellationToken cancellationToken)
    {
        var (alert, run, _, now) = locked;
        // Wall-clock correction may temporarily precede a previously committed instant. Wait for a later poll; never invent time.
        if (run.State is EscalationRunState.Completed or EscalationRunState.Stopped || run.LeaseExpiresAtUtc > now
            || now < (run.UpdatedAtUtc ?? run.StartedAtUtc)) return false;
        if (alert.State is AlertState.Resolved or AlertState.Cancelled) return true;
        if (await db.ResponsibilityAssignments.AnyAsync(x => x.OrganizationId == run.OrganizationId && x.AlertId == run.AlertId
            && x.AlertVersion == run.AlertVersion && x.ReleasedAtUtc == null && x.AcceptedAtUtc <= now, cancellationToken)) return true;
        if (alert.State != AlertState.Active || run.State == EscalationRunState.Paused) return false;
        if (run.NextDueAtUtc <= now) return true;
        if (run.ConsumedSignals.Any(s => s.StepSequence == run.CurrentStep)) return false;
        var consumed = run.ConsumedSignals.Select(s => s.ResponseId).ToArray();
        return await db.RecipientResponses.AnyAsync(s => s.OrganizationId == run.OrganizationId && s.AlertId == run.AlertId && s.AlertVersion == run.AlertVersion
            && (s.ResponseType == RecipientResponseType.Declined || s.ResponseType == RecipientResponseType.Unavailable) && s.OccurredAtUtc <= now
            && !consumed.Contains(s.Id) && db.AlertRecipientSelections.Any(v => v.OrganizationId == s.OrganizationId && v.AlertId == s.AlertId
                && v.AlertVersion == s.AlertVersion && v.PractitionerId == s.PractitionerId && v.SelectedAtUtc <= s.OccurredAtUtc), cancellationToken);
    }

    internal static bool Matches(Alert alert, AlertEscalationPlan plan)
        => alert.AutomaticEscalationEligible && alert.Id == plan.AlertId && alert.OrganizationId == plan.OrganizationId
            && alert.DraftVersion.Value == plan.AlertVersion && alert.ExactEscalationPlanId == plan.Id
            && alert.ExactEscalationPlanRevision == plan.Revision && alert.ExactEscalationPolicyId == plan.EscalationPolicyId
            && alert.ExactEscalationPolicyVersion == plan.EscalationPolicyVersion;
}
