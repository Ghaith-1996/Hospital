using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Directory;

public sealed class DirectorySearchService(CriticalAlertsDbContext db, TimeProvider time) : IDirectorySearchService
{
    public async Task<IReadOnlyList<DirectoryPractitionerListItem>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var practitioners = db.Practitioners
            .AsNoTracking()
            .Where(practitioner => practitioner.OrganizationId == query.OrganizationId);
        if (!query.IncludeInactive)
        {
            practitioners = practitioners.Where(practitioner => practitioner.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var term = query.Text.Trim();
            practitioners = practitioners.Where(practitioner =>
                EF.Functions.ILike(practitioner.LastName, $"%{term}%")
                || EF.Functions.ILike(practitioner.FirstName, $"%{term}%")
                || EF.Functions.ILike(practitioner.Specialty, $"%{term}%")
                || EF.Functions.ILike(practitioner.SimulationCode, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Department))
        {
            var department = query.Department.Trim();
            practitioners = practitioners.Where(practitioner => db.PractitionerRoles.Any(role =>
                role.OrganizationId == query.OrganizationId
                && role.PractitionerId == practitioner.Id
                && db.Departments.Any(candidate =>
                    candidate.OrganizationId == query.OrganizationId
                    && candidate.Id == role.DepartmentId
                    && (EF.Functions.ILike(candidate.Name, $"%{department}%")
                        || EF.Functions.ILike(candidate.SimulationCode, $"%{department}%")))));
        }

        if (!string.IsNullOrWhiteSpace(query.Site))
        {
            var site = query.Site.Trim();
            practitioners = practitioners.Where(practitioner =>
                db.PractitionerRoles.Any(role =>
                    role.OrganizationId == query.OrganizationId
                    && role.PractitionerId == practitioner.Id
                    && db.Departments.Any(department =>
                        department.OrganizationId == query.OrganizationId
                        && department.Id == role.DepartmentId
                        && db.Sites.Any(candidate =>
                            candidate.OrganizationId == query.OrganizationId
                            && candidate.Id == department.SiteId
                            && (EF.Functions.ILike(candidate.Name, $"%{site}%")
                                || EF.Functions.ILike(candidate.SimulationCode, $"%{site}%"))))));
        }

        if (query.OnCallNow is not null)
        {
            practitioners = query.OnCallNow.Value
                ? practitioners.Where(practitioner => db.OnCallAssignments.Any(assignment =>
                    assignment.OrganizationId == query.OrganizationId
                    && assignment.PractitionerId == practitioner.Id
                    && assignment.StartsAtUtc <= now
                    && now < assignment.EndsAtUtc))
                : practitioners.Where(practitioner => !db.OnCallAssignments.Any(assignment =>
                    assignment.OrganizationId == query.OrganizationId
                    && assignment.PractitionerId == practitioner.Id
                    && assignment.StartsAtUtc <= now
                    && now < assignment.EndsAtUtc));
        }

        var matches = await practitioners
            .OrderBy(practitioner => practitioner.LastName)
            .ThenBy(practitioner => practitioner.FirstName)
            .ThenBy(practitioner => practitioner.SimulationCode)
            .ToListAsync(cancellationToken);
        var practitionerIds = matches.Select(practitioner => practitioner.Id).ToArray();
        var roles = await db.PractitionerRoles
            .AsNoTracking()
            .Where(role => role.OrganizationId == query.OrganizationId && practitionerIds.Contains(role.PractitionerId))
            .ToListAsync(cancellationToken);
        var departmentIds = roles.Select(role => role.DepartmentId).Distinct().ToArray();
        var departments = await db.Departments
            .AsNoTracking()
            .Where(department => department.OrganizationId == query.OrganizationId && departmentIds.Contains(department.Id))
            .ToListAsync(cancellationToken);
        var siteIds = departments.Select(department => department.SiteId).Distinct().ToArray();
        var sites = await db.Sites
            .AsNoTracking()
            .Where(site => site.OrganizationId == query.OrganizationId && siteIds.Contains(site.Id))
            .ToListAsync(cancellationToken);
        var sourceRecords = (await db.DirectorySourceRecords
            .AsNoTracking()
            .Where(record => record.OrganizationId == query.OrganizationId)
            .ToListAsync(cancellationToken))
            .Where(record => record.PractitionerId is PractitionerId mapped && practitionerIds.Contains(mapped))
            .ToList();
        var onCall = await db.OnCallAssignments
            .AsNoTracking()
            .Where(assignment => assignment.OrganizationId == query.OrganizationId && practitionerIds.Contains(assignment.PractitionerId))
            .ToListAsync(cancellationToken);
        var endpointKinds = await db.ContactEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.OrganizationId == query.OrganizationId
                && practitionerIds.Contains(endpoint.PractitionerId)
                && endpoint.IsActive)
            .Select(endpoint => new { endpoint.PractitionerId, endpoint.Kind })
            .ToListAsync(cancellationToken);

        return matches.Select(practitioner =>
        {
            var practitionerRoles = roles
                .Where(role => role.PractitionerId == practitioner.Id)
                .OrderByDescending(role => role.IsPrimary)
                .ThenBy(role => role.Title)
                .ThenBy(role => role.Id.Value)
                .ToArray();
            var primaryRole = practitionerRoles.FirstOrDefault();
            var department = primaryRole is null
                ? null
                : departments.SingleOrDefault(item => item.Id == primaryRole.DepartmentId);
            var site = department is null
                ? null
                : sites.SingleOrDefault(item => item.Id == department.SiteId);
            var latestSource = sourceRecords
                .Where(record => record.PractitionerId == practitioner.Id)
                .OrderByDescending(record => record.LastSeenAtUtc)
                .FirstOrDefault();
            var latestOnCall = onCall
                .Where(assignment => assignment.PractitionerId == practitioner.Id
                        && assignment.StartsAtUtc <= now
                        && now < assignment.EndsAtUtc)
                .OrderByDescending(assignment => assignment.LastSynchronizedAtUtc)
                .FirstOrDefault();
            var availableChannels = endpointKinds
                .Where(endpoint => endpoint.PractitionerId == practitioner.Id)
                .Select(endpoint => ToNotificationChannel(endpoint.Kind))
                .Distinct()
                .OrderBy(channel => channel)
                .ToArray();
            var selectionRevision = DirectorySelectionRevision.Compute(new DirectorySelectionRevisionSnapshot(
                query.OrganizationId,
                practitioner.Id,
                practitioner.Specialty,
                practitioner.IsActive,
                practitionerRoles
                    .Select(role => new DirectoryRoleRevision(role.Id, role.DepartmentId, role.Title, role.IsPrimary))
                    .ToArray(),
                sourceRecords
                    .Where(record => record.PractitionerId == practitioner.Id)
                    .Select(record => new DirectorySourceRevision(
                        record.SourceSystem,
                        record.SourceRecordId,
                        record.SourceUpdatedAtUtc,
                        record.LastSeenAtUtc,
                        record.IsStale))
                    .ToArray(),
                onCall
                    .Where(assignment => assignment.PractitionerId == practitioner.Id
                        && assignment.StartsAtUtc <= now
                        && now < assignment.EndsAtUtc)
                    .Select(assignment => new DirectoryOnCallRevision(
                        assignment.SiteId,
                        assignment.DepartmentId,
                        assignment.Tier,
                        assignment.StartsAtUtc,
                        assignment.EndsAtUtc,
                        assignment.LastSynchronizedAtUtc))
                    .ToArray(),
                availableChannels));
            return new DirectoryPractitionerListItem(
                practitioner.Id.Value,
                $"{practitioner.FirstName} {practitioner.LastName}",
                practitioner.FirstName,
                practitioner.LastName,
                practitioner.Specialty,
                department?.Name,
                site?.Name,
                primaryRole?.Title,
                practitioner.SimulationCode,
                practitioner.IsActive,
                latestSource?.IsStale ?? false,
                practitioner.IsActive,
                latestSource?.SourceSystem,
                latestSource?.LastSeenAtUtc,
                latestOnCall?.Tier.ToString(),
                latestOnCall?.SourceSystem,
                latestOnCall?.LastSynchronizedAtUtc,
                primaryRole?.Id.Value,
                availableChannels.Select(channel => channel.ToString()).ToArray(),
                selectionRevision);
        }).ToArray();
    }

    private static NotificationChannel ToNotificationChannel(ContactEndpointKind kind)
        => kind switch
        {
            ContactEndpointKind.SecureMessage => NotificationChannel.SecureMessage,
            ContactEndpointKind.Sms => NotificationChannel.Sms,
            ContactEndpointKind.Voice => NotificationChannel.Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported contact endpoint kind."),
        };
}
