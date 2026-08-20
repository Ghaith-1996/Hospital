namespace CriticalAlerts.Application.Identity;

public interface IDevelopmentIdentityDirectory
{
    Task<SeededIdentity?> FindActiveByHandleAsync(string simulationHandle, CancellationToken cancellationToken);

    Task<IReadOnlyList<SeededIdentity>> ListActiveAsync(CancellationToken cancellationToken);
}
