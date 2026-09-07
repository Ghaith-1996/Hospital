using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Escalation;

public sealed class EscalationPlanResolver(CriticalAlertsDbContext db)
{
    // AlertMutationLock must precede this lock. SHARE blocks inserts, updates and deletes,
    // including phantom directory/policy rows, until confirmation has committed.
    internal Task LockEvidenceAsync(CancellationToken cancellationToken)
        => db.Database.ExecuteSqlRawAsync(
            "LOCK TABLE contact_endpoints, departments, directory_source_records, escalation_policies, escalation_steps, on_call_assignments, practitioner_roles, practitioners, sites IN SHARE MODE",
            cancellationToken);

    public async Task<EscalationPlanDefinition> ResolveAsync(Alert alert, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organization = alert.OrganizationId;
        var policies = await db.EscalationPolicies.AsNoTracking().Where(p => p.OrganizationId == organization && p.IsActive).ToArrayAsync(cancellationToken);
        if (policies.Length != 1) throw InvalidPlan();
        var policy = policies[0];
        var steps = await db.EscalationSteps.AsNoTracking().Where(s => s.OrganizationId == organization && s.PolicyId == policy.Id)
            .OrderBy(s => s.SequenceNumber).ToArrayAsync(cancellationToken);
        var roles = await db.PractitionerRoles.AsNoTracking().Where(r => r.OrganizationId == organization).OrderBy(r => r.Id).ToArrayAsync(cancellationToken);
        var backupRoles = steps.Select(step =>
        {
            const string prefix = "DEMO-role:";
            if (!step.RecipientSource.StartsWith(prefix, StringComparison.Ordinal)
                || !Guid.TryParse(step.RecipientSource[prefix.Length..], out var roleId)) throw InvalidPlan();
            return roles.SingleOrDefault(r => r.Id == new PractitionerRoleId(roleId)) ?? throw InvalidPlan();
        }).ToArray();
        var ids = alert.CurrentRecipients.Select(r => r.PractitionerId).Concat(backupRoles.Select(r => r.PractitionerId)).Distinct().ToArray();
        var practitioners = await db.Practitioners.AsNoTracking().Where(p => p.OrganizationId == organization && ids.Contains(p.Id)).OrderBy(p => p.Id).ToArrayAsync(cancellationToken);
        if (practitioners.Length != ids.Length || practitioners.Any(p => !p.IsActive)) throw InvalidPlan();
        var sources = (await db.DirectorySourceRecords.AsNoTracking().Where(s => s.OrganizationId == organization).OrderBy(s => s.Id).ToArrayAsync(cancellationToken))
            .Where(s => s.PractitionerId is { } id && ids.Contains(id)).ToArray();
        var onCall = await db.OnCallAssignments.AsNoTracking().Where(o => o.OrganizationId == organization && ids.Contains(o.PractitionerId)).OrderBy(o => o.Id).ToArrayAsync(cancellationToken);
        var endpoints = await db.ContactEndpoints.AsNoTracking().Where(e => e.OrganizationId == organization && ids.Contains(e.PractitionerId))
            .OrderBy(e => e.Id).Select(e => new { e.Id, e.PractitionerId, e.Kind, e.IsActive, e.IsPrimary, e.SourceSystem, e.SourceRecordId }).ToArrayAsync(cancellationToken);
        var departments = await db.Departments.AsNoTracking().Where(d => d.OrganizationId == organization).OrderBy(d => d.Id).ToArrayAsync(cancellationToken);
        var sites = await db.Sites.AsNoTracking().Where(s => s.OrganizationId == organization).OrderBy(s => s.Id).ToArrayAsync(cancellationToken);
        string Evidence(PractitionerId id) => Hash(new
        {
            practitioner = practitioners.Single(p => p.Id == id),
            roles = roles.Where(r => r.PractitionerId == id),
            sources = sources.Where(s => s.PractitionerId == id),
            onCall = onCall.Where(o => o.PractitionerId == id).Select(o => new { assignment = o, active = o.StartsAtUtc <= now && now < o.EndsAtUtc }),
            endpoints = endpoints.Where(e => e.PractitionerId == id),
            departments,
            sites,
        });
        string OnCallSummary(PractitionerId id)
        {
            var activeTiers = onCall.Where(o => o.PractitionerId == id && o.StartsAtUtc <= now && now < o.EndsAtUtc)
                .Select(o => o.Tier.ToString()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();
            return activeTiers.Length == 0 ? "No active on-call assignment" : $"DEMO on-call: {string.Join(", ", activeTiers)}";
        }
        var pairs = alert.CurrentRecipients.Select(r => (r.PractitionerId, r.Channel)).ToHashSet();
        var snapshots = steps.Select((step, index) =>
        {
            var role = backupRoles[index];
            var practitioner = practitioners.Single(p => p.Id == role.PractitionerId);
            var channels = step.Channels.Split(',', StringSplitOptions.TrimEntries);
            var recipients = channels.Select(value =>
            {
                if (!Enum.TryParse<NotificationChannel>(value, out var channel) || !Enum.IsDefined(channel)
                    || !pairs.Add((practitioner.Id, channel))
                    || !endpoints.Any(e => e.PractitionerId == practitioner.Id && e.IsActive && e.Kind.ToString() == channel.ToString())) throw InvalidPlan();
                return new EscalationRecipientEvidence(practitioner.Id.Value, role.Id.Value,
                    $"{practitioner.FirstName} {practitioner.LastName}", role.Title, channel.ToString(), Evidence(practitioner.Id),
                    sources.Where(s => s.PractitionerId == practitioner.Id).OrderByDescending(s => s.SourceUpdatedAtUtc).FirstOrDefault()?.SourceUpdatedAtUtc,
                    OnCallSummary(practitioner.Id));
            }).ToArray();
            if (step.Delay.Ticks % TimeSpan.TicksPerSecond != 0) throw InvalidPlan();
            return new EscalationStepSnapshot(step.SequenceNumber, (long)step.Delay.TotalSeconds, step.MaxAttempts, step.RecipientSource, recipients);
        }).ToArray();
        var definition = new EscalationPlanDefinition(organization.Value, alert.Id.Value, alert.DraftVersion.Value, policy.Id.Value,
            policy.Version, policy.TriggerCondition, policy.StopCondition,
            Hash(alert.CurrentRecipients.OrderBy(r => r.PractitionerId.Value).ThenBy(r => r.Channel)
                .Select(r => new { r.PractitionerId, r.PractitionerRoleId, r.Channel, revision = Evidence(r.PractitionerId) })), snapshots);
        try { _ = AlertEscalationPlan.CanonicalJson(definition); }
        catch (DomainException) { throw InvalidPlan(); }
        return definition;
    }

    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static AlertReviewValidationException InvalidPlan() => new("escalation-plan-invalid",
        "The fixed DEMO escalation plan is unavailable, invalid or overlaps selected recipients. Reload review and correct the explicit policy or recipient selection; no replacement is selected.");
}
