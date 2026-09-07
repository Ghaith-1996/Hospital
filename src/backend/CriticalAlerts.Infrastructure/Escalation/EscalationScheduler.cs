using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed class EscalationScheduler(CriticalAlertsDbContext db)
{
    public async Task<bool> ScheduleNextAsync(CancellationToken cancellationToken = default)
    {
        // Discovery is read-only. No row is locked until its alert has been selected.
        // Reject unsupported immutable rules before LIMIT so ineligible rows cannot starve later work.
        var ids = (await db.Database.SqlQuery<Guid>($"""
            SELECT a.id AS "Value" FROM alerts a
            JOIN alert_escalation_plans p ON p.id = a.exact_escalation_plan_id AND p.organization_id = a.organization_id
              AND p.alert_id = a.id AND p.alert_version = a.draft_version AND p.revision = a.exact_escalation_plan_revision
              AND p.escalation_policy_id = a.exact_escalation_policy_id AND p.escalation_policy_version = a.exact_escalation_policy_version
            WHERE a.state = 'Active' AND a.confirmed_draft_version = a.draft_version
              AND p.definition_json::jsonb ->> 'TriggerCondition' = {DemoEscalationSemantics.TriggerCondition}
              AND p.definition_json::jsonb ->> 'StopCondition' = {DemoEscalationSemantics.StopCondition}
              AND NOT EXISTS (SELECT 1 FROM escalation_runs r WHERE r.organization_id = a.organization_id
                AND r.alert_id = a.id AND r.alert_version = a.draft_version)
            ORDER BY a.confirmed_at_utc, a.id LIMIT 100
            """).ToArrayAsync(cancellationToken)).Select(id => new AlertId(id)).ToArray();
        var candidates = await db.Alerts.AsNoTracking().Where(a => ids.Contains(a.Id))
            .OrderBy(a => a.ConfirmedAtUtc).ThenBy(a => a.Id).Select(a => new { a.OrganizationId, a.Id }).ToArrayAsync(cancellationToken);
        foreach (var candidate in candidates)
            if (await ScheduleAsync(candidate.OrganizationId, candidate.Id, cancellationToken)) return true;
        return false;
    }

    public async Task<bool> ScheduleAsync(OrganizationId organizationId, AlertId alertId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AlertMutationLock.TryAcquireAsync(db, organizationId, alertId, cancellationToken)) return false;
        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.Id == alertId, cancellationToken);
        if (alert is null) return false;
        await db.Entry(alert).ReloadAsync(cancellationToken);
        if (alert.State != AlertState.Active || !alert.AutomaticEscalationEligible
            || await db.EscalationRuns.AnyAsync(r => r.OrganizationId == organizationId && r.AlertId == alertId && r.AlertVersion == alert.DraftVersion, cancellationToken)) return false;
        var plan = await db.AlertEscalationPlans.AsNoTracking().SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == alert.ExactEscalationPlanId, cancellationToken);
        if (plan is null || !EscalationRunRepository.Matches(alert, plan) || !DemoEscalationSemantics.IsSupported(plan.Definition)) return false;
        var now = await new DatabaseClock(db).GetUtcNowAsync(cancellationToken);
        var run = EscalationRun.Schedule(EscalationRunId.New(), plan, alert.DraftVersion, now);
        var correlation = Guid.NewGuid();
        db.EscalationRuns.Add(run);
        db.EscalationEvents.Add(EscalationEvent.Record(run, EscalationEventType.Scheduled, 1, null, correlation, now));
        db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), organizationId, "SimulationWorker", null,
            "escalation-scheduled", "EscalationRun", run.Id.Value, "scheduled", correlation.ToString("D"), "{}", now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
