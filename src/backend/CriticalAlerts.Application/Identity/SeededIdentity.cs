namespace CriticalAlerts.Application.Identity;

public sealed record SeededIdentity(
    Guid UserId,
    Guid OrganizationId,
    string DisplayName,
    string SimulationHandle,
    IReadOnlyList<string> Roles,
    bool IsActive);
