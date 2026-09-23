using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Assistance;

namespace CriticalAlerts.Application.Assistance;

public sealed record AssistanceRequest(int ExpectedVersion);
public sealed record AssistanceApplyResult(Guid AlertId, Guid ResultId, int DraftVersion, bool Replayed);
public sealed record AssistanceResultView(Guid Id, Guid AlertId, int AlertVersion, Guid SourceRevisionId, DateTimeOffset CreatedAtUtc,
    string Kind, string Provider, string ProviderVersion, string ConfigurationVersion, bool Stale,
    string SourceText, TranscriptionResult? Transcription, AlertStructuringSuggestion? Suggestion);
public sealed record AssistancePage(IReadOnlyList<AssistanceResultView> Items, string? NextCursor);
public interface IAssistanceService
{
    Task<AssistanceResultView> GenerateAsync(OrganizationId organization, UserId actor, string correlation, AlertId alert,
        AssistanceKind kind, int expectedVersion, string? key, byte[]? audio, string? contentType,
        TranscriptionOptions options, CancellationToken cancellationToken);
    Task<AssistanceApplyResult> ApplyAsync(OrganizationId organization, UserId actor, string correlation, AlertId alert,
        AssistanceKind kind, Guid result, int expectedVersion, string? key, CancellationToken cancellationToken);
    Task<AssistancePage> GetAsync(OrganizationId organization, AlertId alert, AssistanceKind kind, string? cursor, CancellationToken cancellationToken);
}
