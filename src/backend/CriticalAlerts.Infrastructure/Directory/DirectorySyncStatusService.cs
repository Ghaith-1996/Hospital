using CriticalAlerts.Application.Directory;
using CriticalAlerts.Domain;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Directory;

public sealed class DirectorySyncStatusService(CriticalAlertsDbContext db) : IDirectorySyncStatusService
{
    public async Task<DirectorySyncStatus> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken)
    {
        var run = await db.DirectorySyncRuns.AsNoTracking().Where(r => r.OrganizationId == organizationId)
            .OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        if (run is null) return new("NotRecorded", "unknown", null, null, 0, 0, 0);
        var source = run.SourceSystem == DirectorySourceSystems.Csv ? DirectorySourceSystems.Csv
            : run.SourceSystem == "SIM-DIRECTORY" ? "SIM-DIRECTORY" : "unknown";
        return new(Enum.IsDefined(run.Status) ? run.Status.ToString() : "Unknown", source,
            run.StartedAtUtc, run.EndedAtUtc, run.InsertedCount, run.UpdatedCount, run.RejectedCount);
    }
}
