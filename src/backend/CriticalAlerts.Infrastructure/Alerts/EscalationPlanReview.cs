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
        if (policies.Length != 1 || policies[0].Version != "DEMO-9")
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
        var department = await db.Departments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OrganizationId == alert.OrganizationId
                && row.Id == alert.DepartmentId && row.SiteId == alert.SiteId, ct);
        var site = await db.Sites.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OrganizationId == alert.OrganizationId && row.Id == alert.SiteId, ct);
        if (department is null || site is null)
        {
            throw Changed();
        }

        var backupIds = backups.Select(row => row.PractitionerId).Distinct().ToArray();
        var matchingRoles = await db.PractitionerRoles.AsNoTracking()
            .Where(row => row.OrganizationId == alert.OrganizationId && row.DepartmentId == alert.DepartmentId
                && backupIds.Contains(row.PractitionerId)).ToArrayAsync(ct);
        var directoryById = directory.ToDictionary(row => row.PractitionerId);
        var manualPairs = alert.CurrentRecipients.Select(row => (row.PractitionerId.Value, row.Channel.ToString())).ToHashSet();
        var recipients = new List<EscalationPlanRecipient>();
        foreach (var backup in backups.OrderByDescending(row => row.LastSynchronizedAtUtc).ThenBy(row => row.Id.Value))
        {
            if (!directoryById.TryGetValue(backup.PractitionerId.Value, out var practitioner)
                || !practitioner.Selectable || practitioner.IsStale
                || !practitioner.AvailableChannels.Contains("SecureMessage")
                || manualPairs.Contains((practitioner.PractitionerId, "SecureMessage"))
                || recipients.Any(row => row.PractitionerId == practitioner.PractitionerId))
            {
                continue;
            }

            var role = matchingRoles.Where(row => row.PractitionerId == backup.PractitionerId)
                .OrderByDescending(row => row.IsPrimary)
                .ThenBy(row => row.Title, StringComparer.Ordinal)
                .ThenBy(row => row.Id.Value)
                .FirstOrDefault();
            if (role is null)
            {
                continue;
            }

            recipients.Add(new EscalationPlanRecipient(practitioner.PractitionerId, role.Id.Value,
                practitioner.DisplayName, practitioner.Specialty, department.Name, site.Name, role.Title,
                "SecureMessage", practitioner.SelectionRevision, backup.LastSynchronizedAtUtc,
                $"Backup {backup.StartsAtUtc:yyyy-MM-ddTHH:mm:ssZ}–{backup.EndsAtUtc:yyyy-MM-ddTHH:mm:ssZ}",
                new EscalationOnCallEvidence(backup.Id.Value, backup.SourceSystem, backup.SourceRecordId,
                    backup.StartsAtUtc, backup.EndsAtUtc, backup.LastSynchronizedAtUtc)));
        }
        recipients.Sort((left, right) => left.PractitionerId.CompareTo(right.PractitionerId));
        // A pair appears once; subsequent steps without additional approved recipients exhaust safely.
        var planSteps = steps.Select((step, index) => new EscalationPlanStepView(step.Id.Value,
            step.SequenceNumber, checked((long)step.Delay.TotalSeconds), index == 0 ? recipients.ToArray() : [])).ToArray();
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
