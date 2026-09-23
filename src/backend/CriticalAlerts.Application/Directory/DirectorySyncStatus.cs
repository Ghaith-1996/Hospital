using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Directory;

public sealed record DirectorySyncStatus(string Status, string SourceSystem, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? EndedAtUtc, int InsertedCount, int UpdatedCount, int RejectedCount);

public interface IDirectorySyncStatusService
{
    Task<DirectorySyncStatus> GetAsync(OrganizationId organizationId, CancellationToken cancellationToken);
}
