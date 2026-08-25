using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Directory;

public sealed class DirectorySearchService(CriticalAlertsDbContext db) : IDirectorySearchService
{
    public async Task<IReadOnlyList<DirectoryPractitionerListItem>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken)
    {
        var practitioners = db.Practitioners.Where(practitioner => practitioner.OrganizationId == query.OrganizationId);
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

        var matches = await practitioners
            .OrderBy(practitioner => practitioner.LastName)
            .ThenBy(practitioner => practitioner.FirstName)
            .ThenBy(practitioner => practitioner.SimulationCode)
            .ToListAsync(cancellationToken);
        var practitionerIds = matches.Select(practitioner => practitioner.Id).ToArray();
        var roles = await db.PractitionerRoles
            .Where(role => role.OrganizationId == query.OrganizationId && practitionerIds.Contains(role.PractitionerId))
            .ToListAsync(cancellationToken);
        var departmentIds = roles.Select(role => role.DepartmentId).Distinct().ToArray();
        var departments = await db.Departments
            .Where(department => department.OrganizationId == query.OrganizationId && departmentIds.Contains(department.Id))
            .ToListAsync(cancellationToken);
        var siteIds = departments.Select(department => department.SiteId).Distinct().ToArray();
        var sites = await db.Sites
            .Where(site => site.OrganizationId == query.OrganizationId && siteIds.Contains(site.Id))
            .ToListAsync(cancellationToken);
        var sourceRecords = (await db.DirectorySourceRecords
            .Where(record => record.OrganizationId == query.OrganizationId)
            .ToListAsync(cancellationToken))
            .Where(record => record.PractitionerId is PractitionerId mapped && practitionerIds.Contains(mapped))
            .ToList();
        var onCall = await db.OnCallAssignments
            .Where(assignment => assignment.OrganizationId == query.OrganizationId && practitionerIds.Contains(assignment.PractitionerId))
            .ToListAsync(cancellationToken);

        return matches.Select(practitioner =>
        {
            var primaryRole = roles
                .Where(role => role.PractitionerId == practitioner.Id)
                .OrderByDescending(role => role.IsPrimary)
                .ThenBy(role => role.Title)
                .FirstOrDefault();
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
                .Where(assignment => assignment.PractitionerId == practitioner.Id)
                .OrderByDescending(assignment => assignment.LastSynchronizedAtUtc)
                .FirstOrDefault();
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
                latestOnCall?.LastSynchronizedAtUtc);
        }).ToArray();
    }
}
