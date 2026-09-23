using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Audit;

public sealed record AuditQuery(
    DateTimeOffset? OccurredFromUtc = null,
    DateTimeOffset? OccurredToUtc = null,
    string? Action = null,
    string? Outcome = null,
    string? ResourceType = null,
    string? CorrelationId = null,
    string? Cursor = null,
    int PageSize = 50)
{
    public void Validate()
    {
        if (PageSize is < 1 or > 100
            || OccurredFromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero
            || OccurredToUtc is { Offset: var toOffset } && toOffset != TimeSpan.Zero
            || OccurredFromUtc is { } from && OccurredToUtc is { } to && from >= to
            || Action is not null && !AuditSafety.Actions.Contains(Action)
            || Outcome is not null && !AuditSafety.Outcomes.Contains(Outcome)
            || ResourceType is not null && !AuditSafety.ResourceTypes.Contains(ResourceType)
            || CorrelationId is not null && !AuditSafety.IsSafeCorrelationId(CorrelationId))
            throw new AuditQueryValidationException();
        if (Cursor is not null) AuditCursor.Decode(Cursor);
    }
}

public sealed record AuditEventView(
    Guid Id, string Action, string ResourceType, Guid ResourceId, string Outcome,
    string? CorrelationId, string ActorType, Guid? ActorUserId, DateTimeOffset OccurredAtUtc,
    IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata);

public sealed record AuditPage(IReadOnlyList<AuditEventView> Events, string? NextCursor);

public interface IAuditQueryService
{
    Task<AuditPage> QueryAsync(OrganizationId organizationId, UserId actorUserId,
        string correlationId, AuditQuery query, CancellationToken cancellationToken);
}

public sealed class AuditQueryValidationException()
    : Exception("Audit query is invalid. Review the filters and pagination.");
