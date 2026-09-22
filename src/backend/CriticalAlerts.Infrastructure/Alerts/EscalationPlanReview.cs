using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Infrastructure.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Alerts;

internal static class EscalationPlanReview
{
    public static Task<DateTimeOffset> DatabaseNowAsync(CriticalAlertsDbContext db, CancellationToken ct)
        => db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    // ponytail: shared table locks keep DEMO evidence stable; use scoped evidence revisions if import throughput matters.
    public static Task LockEvidenceAsync(CriticalAlertsDbContext db, CancellationToken ct)
        => db.Database.ExecuteSqlRawAsync(
            "LOCK TABLE escalation_policies, escalation_steps, practitioners, practitioner_roles, " +
            "directory_source_records, on_call_assignments, contact_endpoints, departments, sites IN SHARE MODE", ct);

    public static async Task<EscalationPlanView> BuildAsync(CriticalAlertsDbContext db, Alert alert, DateTimeOffset now, CancellationToken ct)
    {
        var policies = await db.EscalationPolicies.AsNoTracking()
            .Where(row => row.OrganizationId == alert.OrganizationId && row.IsActive).ToArrayAsync(ct);
        if (policies.Length != 1 || !policies[0].Version.StartsWith("DEMO", StringComparison.Ordinal))
        {
            throw Changed();
        }

        var policy = policies[0];
        var steps = await db.EscalationSteps.AsNoTracking()
            .Where(row => row.OrganizationId == alert.OrganizationId && row.PolicyId == policy.Id)
            .OrderBy(row => row.SequenceNumber).ToArrayAsync(ct);
        if (steps.Length == 0 || steps.Where((step, index) => step.SequenceNumber != index + 1
                || step.Delay < TimeSpan.Zero || step.Channels != "SecureMessage" || step.MaxAttempts != 1).Any())
        {
            throw Changed();
        }

        var directory = await new DirectorySearchService(db, new ReviewClock(now))
            .SearchAsync(new DirectorySearchQuery(alert.OrganizationId, null, false), ct);
        var backups = await db.OnCallAssignments.AsNoTracking()
            .Where(row => row.OrganizationId == alert.OrganizationId && row.SiteId == alert.SiteId
                && row.DepartmentId == alert.DepartmentId && row.Tier == OnCallTier.Backup
                && row.StartsAtUtc <= now && now < row.EndsAtUtc).ToArrayAsync(ct);
        var manualPairs = alert.CurrentRecipients.Select(row => (row.PractitionerId.Value, row.Channel.ToString())).ToHashSet();
        var recipients = directory.Where(row => backups.Any(backup => backup.PractitionerId.Value == row.PractitionerId)
                && row.Selectable && !row.IsStale && row.AvailableChannels.Contains("SecureMessage")
                && !manualPairs.Contains((row.PractitionerId, "SecureMessage")))
            .OrderBy(row => row.PractitionerId)
            .Select(row => new EscalationPlanRecipient(row.PractitionerId, row.PractitionerRoleId,
                row.DisplayName, row.Specialty, row.Department, row.Site, row.RoleTitle, "SecureMessage",
                row.SelectionRevision, row.LastSynchronizedAtUtc, "Backup")).ToArray();
        // A pair appears once; subsequent steps without additional approved recipients exhaust safely.
        var planSteps = steps.Select((step, index) => new EscalationPlanStepView(step.Id.Value,
            step.SequenceNumber, checked((long)step.Delay.TotalSeconds), index == 0 ? recipients : [])).ToArray();
        var manualEvidence = alert.CurrentRecipients.OrderBy(row => row.PractitionerId.Value).ThenBy(row => row.Channel)
            .Select(row => new
            {
                practitionerId = row.PractitionerId.Value,
                roleId = row.PractitionerRoleId?.Value,
                channel = row.Channel.ToString(),
                revision = directory.SingleOrDefault(item => item.PractitionerId == row.PractitionerId.Value)?.SelectionRevision,
            }).ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            organizationId = alert.OrganizationId.Value,
            alertId = alert.Id.Value,
            version = alert.DraftVersion.Value,
            policyId = policy.Id.Value,
            policy.Version,
            policy.TriggerCondition,
            policy.StopCondition,
            policySteps = steps.Select(step => new { step.RecipientSource, step.Channels, step.MaxAttempts }),
            steps = planSteps,
            manualEvidence,
        });
        return new EscalationPlanView(policy.Id.Value, policy.Version,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))), planSteps);
    }

    public static AlertReviewValidationException Changed()
        => new("escalation-plan-changed", "The exact DEMO escalation plan must be reviewed again before confirmation.");

    private sealed class ReviewClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
