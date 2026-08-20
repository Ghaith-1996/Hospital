using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Identity;

public sealed class DevelopmentIdentityDirectory(CriticalAlertsDbContext db) : IDevelopmentIdentityDirectory
{
    public async Task<SeededIdentity?> FindActiveByHandleAsync(string simulationHandle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(simulationHandle))
        {
            return null;
        }

        var handle = simulationHandle.Trim();
        var user = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.SimulationHandle == handle && candidate.IsActive, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await LoadRolesAsync(user.Id, user.OrganizationId, cancellationToken);
        return ToIdentity(user, roles);
    }

    public async Task<IReadOnlyList<SeededIdentity>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking()
            .Where(user => user.IsActive)
            .OrderBy(user => user.DisplayName)
            .ToArrayAsync(cancellationToken);

        var identities = new List<SeededIdentity>(users.Length);
        foreach (var user in users)
        {
            var roles = await LoadRolesAsync(user.Id, user.OrganizationId, cancellationToken);
            identities.Add(ToIdentity(user, roles));
        }

        return identities;
    }

    private async Task<string[]> LoadRolesAsync(UserId userId, OrganizationId organizationId, CancellationToken cancellationToken)
    {
        return await (
                from assignment in db.UserRoles.AsNoTracking()
                join role in db.Roles.AsNoTracking() on assignment.RoleId equals role.Id
                where assignment.UserId == userId
                    && assignment.OrganizationId == organizationId
                    && role.OrganizationId == organizationId
                select role.Name)
            .OrderBy(name => name)
            .ToArrayAsync(cancellationToken);
    }

    private static SeededIdentity ToIdentity(UserAccount user, string[] roles)
        => new(user.Id.Value, user.OrganizationId.Value, user.DisplayName, user.SimulationHandle, roles, user.IsActive);
}
