using System.Text.Json;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Escalation;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Audit;

public sealed class AuditQueryService(CriticalAlertsDbContext db) : IAuditQueryService
{
    public async Task<AuditPage> QueryAsync(OrganizationId organizationId, UserId actorUserId,
        string correlationId, AuditQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        IQueryable<AuditEvent> source = db.AuditEvents;
        if (query.Cursor is { } encoded)
        {
            var cursor = AuditCursor.Decode(encoded);
            // PostgreSQL tuple comparison uses the same timestamp/UUID order as ORDER BY.
            source = db.AuditEvents.FromSqlInterpolated(
                $"SELECT * FROM audit_events WHERE organization_id = {organizationId.Value} AND (occurred_at_utc, id) < ({cursor.OccurredAtUtc}, {cursor.Id})");
        }
        source = source.AsNoTracking().Where(e => e.OrganizationId == organizationId);
        if (query.OccurredFromUtc is { } from) source = source.Where(e => e.OccurredAtUtc >= from);
        if (query.OccurredToUtc is { } to) source = source.Where(e => e.OccurredAtUtc < to);
        if (query.Action is { } action) source = source.Where(e => e.Action == action);
        if (query.Outcome is { } outcome) source = source.Where(e => e.Outcome == outcome);
        if (query.ResourceType is { } type) source = source.Where(e => e.ResourceType == type);
        if (query.CorrelationId is { } correlation) source = source.Where(e => e.CorrelationId == correlation);

        var rows = await source.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Take(query.PageSize + 1).ToArrayAsync(cancellationToken);
        var pageRows = rows.Take(query.PageSize).ToArray();
        var nextCursor = rows.Length > query.PageSize
            ? AuditCursor.Encode(pageRows[^1].OccurredAtUtc, pageRows[^1].Id.Value) : null;
        var result = new AuditPage(pageRows.Select(AuditSafety.Project).ToArray(), nextCursor);

        var filtersUsed = new List<string>();
        if (query.OccurredFromUtc is not null) filtersUsed.Add("occurredFromUtc");
        if (query.OccurredToUtc is not null) filtersUsed.Add("occurredToUtc");
        if (query.Action is not null) filtersUsed.Add("action");
        if (query.Outcome is not null) filtersUsed.Add("outcome");
        if (query.ResourceType is not null) filtersUsed.Add("resourceType");
        if (query.CorrelationId is not null) filtersUsed.Add("correlationId");
        db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), organizationId, "user", actorUserId,
            "audit.read", "audit", Guid.NewGuid(), "succeeded", correlationId,
            JsonSerializer.Serialize(new { pageSize = query.PageSize, filtersUsed, resultCount = result.Events.Count }),
            await new DatabaseClock(db).GetUtcNowAsync(cancellationToken)));
        // A failed append must not yield an unaudited successful response.
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
