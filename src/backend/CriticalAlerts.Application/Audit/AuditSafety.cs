using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Text.Json;
using CriticalAlerts.Domain.Reliability;

namespace CriticalAlerts.Application.Audit;

public sealed record AuditCursor(DateTimeOffset OccurredAtUtc, Guid Id)
{
    public static string Encode(DateTimeOffset occurredAtUtc, Guid id)
    {
        if (occurredAtUtc.Offset != TimeSpan.Zero || id == Guid.Empty)
            throw new AuditQueryValidationException();
        Span<byte> bytes = stackalloc byte[24];
        BinaryPrimitives.WriteInt64BigEndian(bytes, occurredAtUtc.Ticks);
        id.TryWriteBytes(bytes[8..]);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');
    }

    public static AuditCursor Decode(string cursor)
    {
        if (cursor.Length != 32 || cursor.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new AuditQueryValidationException();
        try
        {
            var bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/'));
            var time = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(bytes), TimeSpan.Zero);
            var id = new Guid(bytes.AsSpan(8));
            if (id == Guid.Empty) throw new AuditQueryValidationException();
            return new AuditCursor(time, id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new AuditQueryValidationException();
        }
    }
}

public static class AuditSafety
{
    public static IReadOnlySet<string> Actions { get; } = new[]
    {
        "alert.draft.created", "alert.draft.updated", "alert.critical-field.confirmed",
        "alert.draft.submitted", "alert.approved-message.updated", "alert.recipients.replaced",
        "alert.confirmed", "alert.resolved", "alert.cancelled", "recipient.opened",
        "recipient.response.acknowledged", "recipient.response.accepted", "recipient.response.declined",
        "recipient.response.unavailable", "recipient.response.callunitrequested", "directory.import.applied",
        "dispatch.suppressed", "dispatch.completed", "dispatch.failed", "dispatch.delivery-event",
        "dispatch.retry-scheduled", "escalation-scheduled", "escalation-recipients-activated", "escalation-dispatch-queued",
        "escalation-stopped", "escalation-exhausted", "escalation-processing-failed",
        "escalation.paused", "escalation.resumed", "audit.read",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> Outcomes { get; } = new[]
    {
        "succeeded", "failed", "scheduled", "activated", "stopped", "exhausted", "completed",
        "ResponsibilityAccepted", "Resolved", "Cancelled", "ProcessingFailed", "Exhausted",
        "manual-fallback", "ConfirmedRecipientUnavailable", "ConfirmedPlanInvalid", "ProcessingError",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> ResourceTypes { get; } = new[]
    {
        "alert", "delivery-attempt", "directory_sync_run", "EscalationRun", "audit",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> CountKeys = new[]
    {
        "version", "alertVersion", "draftVersion", "recipientCount", "attemptNumber", "retryCount",
        "resultCount", "pageSize", "inserted", "updated", "rejected", "stepSequence", "secureMessageAttemptCount",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Channels = new[] { "SecureMessage", "Sms", "Voice" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> ResponseTypes = new[]
    {
        "Acknowledged", "Accepted", "Declined", "Unavailable", "CallUnitRequested",
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> FilterNames = new[]
    {
        "occurredFromUtc", "occurredToUtc", "action", "outcome", "resourceType", "correlationId",
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> Actors = new[] { "user", "worker", "SimulationWorker", "system" }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsSafeCorrelationId(string? value)
        => value is not null && (value.Length == 32 && Guid.TryParseExact(value, "N", out _)
            || value.Length == 36 && Guid.TryParseExact(value, "D", out _));

    public static AuditEventView Project(AuditEvent row) => new(
        row.Id.Value, Allowed(row.Action, Actions), Allowed(row.ResourceType, ResourceTypes), row.ResourceId,
        Allowed(row.Outcome, Outcomes), IsSafeCorrelationId(row.CorrelationId) ? row.CorrelationId : null,
        Allowed(row.ActorType, Actors), row.ActorUserId?.Value, row.OccurredAtUtc, ProjectMetadata(row.SanitizedMetadata));

    public static IReadOnlyDictionary<string, JsonElement> ProjectMetadata(string? metadata)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (metadata is null || metadata.Length > 8192) return result;
        try
        {
            using var document = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) return result;
            foreach (var property in properties)
            {
                var value = property.Value;
                var permitted = CountKeys.Contains(property.Name)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count is >= 0 and <= 1000000;
                permitted |= property.Name == "simulationOnly" && value.ValueKind is JsonValueKind.True or JsonValueKind.False;
                permitted |= property.Name == "channel" && IsAllowedString(value, Channels);
                permitted |= property.Name == "responseType" && IsAllowedString(value, ResponseTypes);
                permitted |= property.Name == "channels" && IsAllowedArray(value, Channels, 3);
                permitted |= property.Name == "filtersUsed" && IsAllowedArray(value, FilterNames, 6);
                if (permitted) result.Add(property.Name, value.Clone());
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
        return result;
    }

    private static string Allowed(string value, IReadOnlySet<string> values) => values.Contains(value) ? value : "unknown";
    private static bool IsAllowedString(JsonElement value, IReadOnlySet<string> values)
        => value.ValueKind == JsonValueKind.String && values.Contains(value.GetString()!);
    private static bool IsAllowedArray(JsonElement value, IReadOnlySet<string> values, int maximum)
        => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= maximum
            && value.EnumerateArray().All(item => IsAllowedString(item, values));
}
