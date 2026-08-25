using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Directory;

public sealed class DirectoryImportService(
    CriticalAlertsDbContext db,
    ISensitiveDataProtector protector,
    TimeProvider time) : IDirectoryImportService
{
    public async Task<DirectoryImportPreviewResult> PreviewAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        Stream source,
        IDirectorySourceAdapter adapter,
        CancellationToken cancellationToken)
    {
        _ = actorUserId;
        _ = correlationId;
        var parsed = adapter.Read(source);
        var catalog = await LoadCatalogAsync(organizationId, adapter.SourceSystem, cancellationToken);
        return Plan(parsed, catalog);
    }

    public async Task<DirectoryImportApplyResult> ApplyAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        Stream source,
        IDirectorySourceAdapter adapter,
        CancellationToken cancellationToken)
    {
        var parsed = adapter.Read(source);
        var catalog = await LoadCatalogAsync(organizationId, adapter.SourceSystem, cancellationToken);
        var preview = Plan(parsed, catalog);
        if (preview.Errors.Count > 0)
        {
            return new DirectoryImportApplyResult(false, null, preview);
        }

        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var syncRun = DirectorySyncRun.Start(
            DirectorySyncRunId.New(),
            organizationId,
            adapter.SourceSystem,
            now,
            correlationId);
        db.DirectorySyncRuns.Add(syncRun);

        foreach (var change in preview.Changes)
        {
            var inbound = parsed.Practitioners.Single(practitioner =>
                string.Equals(practitioner.SourceRecordId, change.SourceRecordId, StringComparison.OrdinalIgnoreCase));
            await ApplyPractitionerAsync(organizationId, adapter.SourceSystem, inbound, catalog, now, cancellationToken);
        }

        syncRun.Complete(
            now,
            preview.InsertCount,
            preview.UpdateCount,
            deactivatedCount: parsed.Practitioners.Count(practitioner => !practitioner.IsActive && preview.Changes.Any(change => change.SourceRecordId == practitioner.SourceRecordId)),
            preview.RejectedCount,
            DirectorySyncRunStatus.Succeeded,
            "none");
        db.AuditEvents.Add(AuditEvent.Record(
            AuditEventId.New(),
            organizationId,
            "user",
            actorUserId,
            "directory.import.applied",
            "directory_sync_run",
            syncRun.Id.Value,
            "succeeded",
            correlationId,
            $$"""{"sourceSystem":"{{adapter.SourceSystem}}","inserted":{{preview.InsertCount}},"updated":{{preview.UpdateCount}},"rejected":{{preview.RejectedCount}}}""",
            now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DirectoryImportApplyResult(true, syncRun.Id.Value, preview);
    }

    private async Task ApplyPractitionerAsync(
        OrganizationId organizationId,
        string sourceSystem,
        NormalizedDirectoryPractitioner inbound,
        DirectoryCatalog catalog,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var match = Match(inbound, catalog);
        Practitioner practitioner;
        if (match.Practitioner is null)
        {
            practitioner = Practitioner.Create(
                PractitionerId.New(),
                organizationId,
                inbound.FirstName,
                inbound.LastName,
                inbound.SimulationCode,
                inbound.Specialty,
                inbound.IsActive,
                now);
            db.Practitioners.Add(practitioner);
            catalog.Practitioners.Add(practitioner);
            catalog.PractitionersByCode[practitioner.SimulationCode] = practitioner;
        }
        else
        {
            practitioner = match.Practitioner;
            practitioner.Reconcile(inbound.FirstName, inbound.LastName, inbound.Specialty, inbound.IsActive);
        }

        if (match.SourceRecord is null)
        {
            var sourceRecord = DirectorySourceRecord.Create(
                DirectorySourceRecordId.New(),
                organizationId,
                practitioner.Id,
                sourceSystem,
                inbound.SourceRecordId,
                inbound.SourceUpdatedAtUtc,
                inbound.PayloadHash,
                now,
                inbound.IsStale ? "stale" : "current",
                inbound.IsStale);
            db.DirectorySourceRecords.Add(sourceRecord);
            catalog.SourceRecords.Add(sourceRecord);
            catalog.SourceRecordsBySourceId[sourceRecord.SourceRecordId] = sourceRecord;
        }
        else
        {
            match.SourceRecord.Refresh(practitioner.Id, inbound.SourceUpdatedAtUtc, inbound.PayloadHash, now, inbound.IsStale);
        }

        await ReplaceChildrenAsync(organizationId, sourceSystem, practitioner, inbound, catalog, now, cancellationToken);
    }

    private async Task ReplaceChildrenAsync(
        OrganizationId organizationId,
        string sourceSystem,
        Practitioner practitioner,
        NormalizedDirectoryPractitioner inbound,
        DirectoryCatalog catalog,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingRoles = await db.PractitionerRoles
            .Where(role => role.OrganizationId == organizationId && role.PractitionerId == practitioner.Id)
            .ToListAsync(cancellationToken);
        db.PractitionerRoles.RemoveRange(existingRoles);
        foreach (var role in inbound.Roles)
        {
            db.PractitionerRoles.Add(PractitionerRoleAssignment.Create(
                PractitionerRoleId.New(),
                organizationId,
                practitioner.Id,
                catalog.DepartmentsByCode[role.DepartmentCode].Id,
                role.Title,
                role.IsPrimary));
        }

        var existingEndpoints = await db.ContactEndpoints
            .Where(endpoint => endpoint.OrganizationId == organizationId && endpoint.PractitionerId == practitioner.Id)
            .ToListAsync(cancellationToken);
        db.ContactEndpoints.RemoveRange(existingEndpoints);
        foreach (var endpoint in inbound.Endpoints)
        {
            db.ContactEndpoints.Add(ContactEndpoint.Create(
                ContactEndpointId.New(),
                organizationId,
                practitioner.Id,
                endpoint.Kind,
                protector.Protect(endpoint.Value, new SensitiveDataContext("contact-endpoint", organizationId.Value)),
                endpoint.Label,
                isPrimary: inbound.Endpoints[0].Label == endpoint.Label));
        }

        var existingOnCall = await db.OnCallAssignments
            .Where(assignment => assignment.OrganizationId == organizationId && assignment.PractitionerId == practitioner.Id)
            .ToListAsync(cancellationToken);
        db.OnCallAssignments.RemoveRange(existingOnCall);
        foreach (var assignment in inbound.OnCallAssignments)
        {
            db.OnCallAssignments.Add(OnCallAssignment.Create(
                OnCallAssignmentId.New(),
                organizationId,
                practitioner.Id,
                catalog.SitesByCode[assignment.SiteCode].Id,
                catalog.DepartmentsByCode[assignment.DepartmentCode].Id,
                assignment.Tier,
                assignment.StartsAtUtc,
                assignment.EndsAtUtc,
                sourceSystem,
                inbound.SourceRecordId,
                now));
        }
    }

    private DirectoryImportPreviewResult Plan(DirectoryParseResult parsed, DirectoryCatalog catalog)
    {
        var errors = parsed.Errors.ToList();
        var warnings = parsed.Warnings.ToList();
        var changes = new List<DirectoryImportChange>();
        if (parsed.Errors.Count > 0)
        {
            return new DirectoryImportPreviewResult(
                parsed.SourceSystem,
                0,
                0,
                0,
                parsed.Errors.Select(error => error.SourceRecordId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                errors,
                warnings,
                changes);
        }

        foreach (var inbound in parsed.Practitioners)
        {
            foreach (var role in inbound.Roles)
            {
                if (!catalog.SitesByCode.ContainsKey(role.SiteCode))
                {
                    errors.Add(new DirectoryImportIssue("unknown-site", inbound.SourceRecordId, null, $"site_code '{role.SiteCode}' is not in the organization directory."));
                }

                if (!catalog.DepartmentsByCode.TryGetValue(role.DepartmentCode, out var department))
                {
                    errors.Add(new DirectoryImportIssue("unknown-department", inbound.SourceRecordId, null, $"department_code '{role.DepartmentCode}' is not in the organization directory."));
                    continue;
                }

                var site = catalog.Sites.SingleOrDefault(item => item.Id == department.SiteId);
                if (site is not null && !string.Equals(site.SimulationCode, role.SiteCode, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new DirectoryImportIssue(
                        "site-department-mismatch",
                        inbound.SourceRecordId,
                        null,
                        $"department_code '{role.DepartmentCode}' belongs to '{site.SimulationCode}', not '{role.SiteCode}'."));
                }
            }

            foreach (var assignment in inbound.OnCallAssignments)
            {
                if (!catalog.SitesByCode.ContainsKey(assignment.SiteCode) || !catalog.DepartmentsByCode.ContainsKey(assignment.DepartmentCode))
                {
                    errors.Add(new DirectoryImportIssue("unknown-on-call-location", inbound.SourceRecordId, null, "On-call assignments require known site and department codes."));
                }
            }

            var collision = catalog.Practitioners.Where(practitioner =>
                string.Equals(practitioner.FirstName, inbound.FirstName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(practitioner.LastName, inbound.LastName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(practitioner.SimulationCode, inbound.SimulationCode, StringComparison.OrdinalIgnoreCase));
            foreach (var existing in collision)
            {
                warnings.Add(new DirectoryImportIssue(
                    "name-collision-not-matched",
                    inbound.SourceRecordId,
                    null,
                    $"Display name '{inbound.FirstName} {inbound.LastName}' already exists as {existing.SimulationCode}. Matching uses source_record_id, then simulation_code, never name."));
            }

            var match = Match(inbound, catalog);
            if (match.Conflict is not null)
            {
                errors.Add(match.Conflict);
                continue;
            }

            var action = match.Practitioner is null ? "insert" : "update";
            changes.Add(new DirectoryImportChange(
                action,
                inbound.SourceRecordId,
                inbound.SimulationCode,
                $"{inbound.FirstName} {inbound.LastName}",
                inbound.IsActive));
        }

        if (errors.Count > 0)
        {
            changes.Clear();
        }

        return new DirectoryImportPreviewResult(
            parsed.SourceSystem,
            parsed.Practitioners.Count,
            changes.Count(change => change.Action == "insert"),
            changes.Count(change => change.Action == "update"),
            errors.Select(error => error.SourceRecordId).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            errors,
            warnings,
            changes);
    }

    private static MatchResult Match(NormalizedDirectoryPractitioner inbound, DirectoryCatalog catalog)
    {
        catalog.SourceRecordsBySourceId.TryGetValue(inbound.SourceRecordId, out var sourceRecord);
        catalog.PractitionersByCode.TryGetValue(inbound.SimulationCode, out var byCode);

        if (sourceRecord is not null)
        {
            var mapped = catalog.Practitioners.SingleOrDefault(practitioner => practitioner.Id == sourceRecord.PractitionerId);
            if (mapped is not null && !string.Equals(mapped.SimulationCode, inbound.SimulationCode, StringComparison.OrdinalIgnoreCase))
            {
                return new MatchResult(
                    mapped,
                    sourceRecord,
                    new DirectoryImportIssue(
                        "simulation-code-immutable",
                        inbound.SourceRecordId,
                        null,
                        "An existing source_record_id cannot change simulation_code. Merge policy is REQUIRES_HOSPITAL_DECISION."));
            }

            return new MatchResult(mapped, sourceRecord, null);
        }

        if (byCode is not null)
        {
            var existingSource = catalog.SourceRecords.FirstOrDefault(record => record.PractitionerId == byCode.Id);
            if (existingSource is not null
                && !string.Equals(existingSource.SourceRecordId, inbound.SourceRecordId, StringComparison.OrdinalIgnoreCase))
            {
                return new MatchResult(
                    byCode,
                    existingSource,
                    new DirectoryImportIssue(
                        "simulation-code-owned",
                        inbound.SourceRecordId,
                        null,
                        $"simulation_code '{inbound.SimulationCode}' is already mapped from '{existingSource.SourceRecordId}'."));
            }

            return new MatchResult(byCode, null, null);
        }

        return new MatchResult(null, null, null);
    }

    private async Task<DirectoryCatalog> LoadCatalogAsync(
        OrganizationId organizationId,
        string sourceSystem,
        CancellationToken cancellationToken)
    {
        var sites = await db.Sites.Where(site => site.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var departments = await db.Departments.Where(department => department.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var practitioners = await db.Practitioners.Where(practitioner => practitioner.OrganizationId == organizationId).ToListAsync(cancellationToken);
        var sourceRecords = await db.DirectorySourceRecords
            .Where(record => record.OrganizationId == organizationId && record.SourceSystem == sourceSystem)
            .ToListAsync(cancellationToken);
        return new DirectoryCatalog(sites, departments, practitioners, sourceRecords);
    }

    private sealed record MatchResult(Practitioner? Practitioner, DirectorySourceRecord? SourceRecord, DirectoryImportIssue? Conflict);

    private sealed class DirectoryCatalog
    {
        public DirectoryCatalog(
            List<Site> sites,
            List<Department> departments,
            List<Practitioner> practitioners,
            List<DirectorySourceRecord> sourceRecords)
        {
            Sites = sites;
            Departments = departments;
            Practitioners = practitioners;
            SourceRecords = sourceRecords;
            SitesByCode = sites.ToDictionary(site => site.SimulationCode, StringComparer.OrdinalIgnoreCase);
            DepartmentsByCode = departments.ToDictionary(department => department.SimulationCode, StringComparer.OrdinalIgnoreCase);
            PractitionersByCode = practitioners.ToDictionary(practitioner => practitioner.SimulationCode, StringComparer.OrdinalIgnoreCase);
            SourceRecordsBySourceId = sourceRecords.ToDictionary(record => record.SourceRecordId, StringComparer.OrdinalIgnoreCase);
        }

        public List<Site> Sites { get; }

        public List<Department> Departments { get; }

        public List<Practitioner> Practitioners { get; }

        public List<DirectorySourceRecord> SourceRecords { get; }

        public Dictionary<string, Site> SitesByCode { get; }

        public Dictionary<string, Department> DepartmentsByCode { get; }

        public Dictionary<string, Practitioner> PractitionersByCode { get; }

        public Dictionary<string, DirectorySourceRecord> SourceRecordsBySourceId { get; }
    }
}
