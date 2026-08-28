using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Directory;

public static class DirectorySourceSystems
{
    public const string Csv = "SIM-CSV";
}

public static class SimulationDirectoryRoles
{
    private static readonly IReadOnlySet<string> AllowedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Emergency physician",
        "Medicine consultant",
        "Surgeon",
        "Cardiology consultant",
        "Neurology consultant",
        "Pediatrics consultant",
    };

    public static bool IsAllowed(string title) => AllowedTitles.Contains(title.Trim());
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
    IReadOnlyList<DirectoryImportChange> Changes,
    string PreviewToken = "");

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
        CancellationToken cancellationToken,
        string? previewToken = null);
}

public sealed record DirectorySearchQuery(
    OrganizationId OrganizationId,
    string? Text,
    string? Department,
    string? Site,
    bool? OnCallNow,
    bool IncludeInactive)
{
    public DirectorySearchQuery(OrganizationId organizationId, string? text, bool includeInactive)
        : this(organizationId, text, null, null, null, includeInactive)
    {
    }
}

public sealed record DirectorySelectionCandidate(
    PractitionerId PractitionerId,
    PractitionerRoleId? PractitionerRoleId,
    NotificationChannel Channel,
    string PresentedRevision);

public sealed record DirectoryRoleRevision(
    PractitionerRoleId PractitionerRoleId,
    DepartmentId DepartmentId,
    string Title,
    bool IsPrimary);

public sealed record DirectorySourceRevision(
    string SourceSystem,
    string SourceRecordId,
    DateTimeOffset SourceUpdatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    bool IsStale);

public sealed record DirectoryOnCallRevision(
    SiteId SiteId,
    DepartmentId DepartmentId,
    OnCallTier Tier,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset LastSynchronizedAtUtc);

public sealed record DirectorySelectionRevisionSnapshot(
    OrganizationId OrganizationId,
    PractitionerId PractitionerId,
    string Specialty,
    bool IsActive,
    IReadOnlyCollection<DirectoryRoleRevision> Roles,
    IReadOnlyCollection<DirectorySourceRevision> SourceRecords,
    IReadOnlyCollection<DirectoryOnCallRevision> ActiveOnCallAssignments,
    IReadOnlyCollection<NotificationChannel> AvailableChannels);

public class DirectorySelectionValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}

public sealed class DirectorySelectionRevisionConflictException()
    : DirectorySelectionValidationException(
        "directory-revision-stale",
        "The selected directory entry changed. Reload and reselect recipients.");

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
    DateTimeOffset? OnCallLastSynchronizedAtUtc,
    Guid? PractitionerRoleId,
    IReadOnlyList<string> AvailableChannels,
    string SelectionRevision);

public interface IDirectorySelectionResolver
{
    Task<IReadOnlyList<Domain.Alerts.ValidatedRecipientSelection>> ResolveAsync(
        OrganizationId organizationId,
        IReadOnlyCollection<DirectorySelectionCandidate> candidates,
        DateTimeOffset selectedAtUtc,
        CancellationToken cancellationToken);
}

public interface IDirectorySearchService
{
    Task<IReadOnlyList<DirectoryPractitionerListItem>> SearchAsync(
        DirectorySearchQuery query,
        CancellationToken cancellationToken);
}
