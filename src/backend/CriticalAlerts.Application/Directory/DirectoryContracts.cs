using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Directory;

public static class DirectorySourceSystems
{
    public const string Csv = "SIM-CSV";
}

public sealed record DirectoryImportIssue(string Code, string SourceRecordId, int? RowNumber, string Message);

public sealed record NormalizedDirectoryRole(
    string SiteCode,
    string DepartmentCode,
    string Title,
    bool IsPrimary);

public sealed record NormalizedDirectoryEndpoint(
    ContactEndpointKind Kind,
    string Value,
    string Label);

public sealed record NormalizedDirectoryOnCall(
    string SiteCode,
    string DepartmentCode,
    OnCallTier Tier,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

public sealed record NormalizedDirectoryPractitioner(
    string SourceRecordId,
    string FirstName,
    string LastName,
    string SimulationCode,
    string Specialty,
    bool IsActive,
    DateTimeOffset SourceUpdatedAtUtc,
    bool IsStale,
    string PayloadHash,
    IReadOnlyList<NormalizedDirectoryRole> Roles,
    IReadOnlyList<NormalizedDirectoryEndpoint> Endpoints,
    IReadOnlyList<NormalizedDirectoryOnCall> OnCallAssignments);

public sealed record DirectoryParseResult(
    string SourceSystem,
    IReadOnlyList<NormalizedDirectoryPractitioner> Practitioners,
    IReadOnlyList<DirectoryImportIssue> Errors,
    IReadOnlyList<DirectoryImportIssue> Warnings);

public interface IDirectorySourceAdapter
{
    string SourceSystem { get; }

    DirectoryParseResult Read(Stream source);
}

public sealed record DirectoryImportChange(
    string Action,
    string SourceRecordId,
    string SimulationCode,
    string DisplayName,
    bool Selectable);

public sealed record DirectoryImportPreviewResult(
    string SourceSystem,
    int ParsedPractitionerCount,
    int InsertCount,
    int UpdateCount,
    int RejectedCount,
    IReadOnlyList<DirectoryImportIssue> Errors,
    IReadOnlyList<DirectoryImportIssue> Warnings,
    IReadOnlyList<DirectoryImportChange> Changes);

public sealed record DirectoryImportApplyResult(
    bool Applied,
    Guid? SyncRunId,
    DirectoryImportPreviewResult Preview);

public interface IDirectoryImportService
{
    Task<DirectoryImportPreviewResult> PreviewAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        Stream source,
        IDirectorySourceAdapter adapter,
        CancellationToken cancellationToken);

    Task<DirectoryImportApplyResult> ApplyAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        Stream source,
        IDirectorySourceAdapter adapter,
        CancellationToken cancellationToken);
}

public sealed record DirectorySearchQuery(OrganizationId OrganizationId, string? Text, bool IncludeInactive);

public sealed record DirectoryPractitionerListItem(
    Guid PractitionerId,
    string DisplayName,
    string FirstName,
    string LastName,
    string Specialty,
    string? Department,
    string? Site,
    string? RoleTitle,
    string SimulationCode,
    bool IsActive,
    bool IsStale,
    bool Selectable,
    string? SourceSystem,
    DateTimeOffset? LastSynchronizedAtUtc,
    string? OnCallTier,
    string? OnCallSourceSystem,
    DateTimeOffset? OnCallLastSynchronizedAtUtc);

public interface IDirectorySearchService
{
    Task<IReadOnlyList<DirectoryPractitionerListItem>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken);
}
