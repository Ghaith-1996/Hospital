using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Responses;

public sealed class PractitionerIdentityResolver(CriticalAlertsDbContext db)
{
    public Task<PractitionerId?> ResolveAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        return db.PractitionerUserLinks
            .AsNoTracking()
            .Where(link => link.OrganizationId == organizationId && link.UserId == userId)
            .Select(link => (PractitionerId?)link.PractitionerId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
