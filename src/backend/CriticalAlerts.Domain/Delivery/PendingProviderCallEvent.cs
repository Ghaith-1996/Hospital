namespace CriticalAlerts.Domain.Delivery;

/// <summary>
/// An authenticated voice call event that arrived before the worker committed the call ID it refers to. It is bound
/// to a committed send by its opaque callback tag and applied (or rejected) by the worker on the attempt's next pass.
/// Holds only opaque identifiers, a closed kind and UTC times: never a number, caller ID or provider text.
/// </summary>
public sealed class PendingProviderCallEvent
{
    public const string AppliedResult = "applied";
    public const string RejectedCallMismatchResult = "rejected-call-mismatch";

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "answered", "playback-completed", "playback-failed", "ended" };
    private static readonly HashSet<string> EndReasons = new(StringComparer.Ordinal)
    {
        "completed", "no-answer", "busy", "declined", "unreachable", "failed", "hung-up",
    };

    private PendingProviderCallEvent()
    {
        Provider = string.Empty;
        CallbackTag = string.Empty;
        ExternalEventId = string.Empty;
        CallId = string.Empty;
        Kind = string.Empty;
    }

    public Guid Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Provider { get; private set; }

    public string CallbackTag { get; private set; }

    public string ExternalEventId { get; private set; }

    public string CallId { get; private set; }

    public string Kind { get; private set; }

    public string? EndReason { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset? AppliedAtUtc { get; private set; }

    public string? Result { get; private set; }

    public static PendingProviderCallEvent Create(
        OrganizationId organizationId,
        string provider,
        string callbackTag,
        string externalEventId,
        string callId,
        string kind,
        string? endReason,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset receivedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(callbackTag)
            || string.IsNullOrWhiteSpace(externalEventId) || string.IsNullOrWhiteSpace(callId)
            || !Kinds.Contains(kind) || (endReason is not null && !EndReasons.Contains(endReason))
            || (kind == "ended") != (endReason is not null))
        {
            throw new DomainException("Pending call events require opaque identifiers and a closed kind.");
        }

        return new PendingProviderCallEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Provider = provider,
            CallbackTag = callbackTag,
            ExternalEventId = externalEventId,
            CallId = callId,
            Kind = kind,
            EndReason = endReason,
            OccurredAtUtc = UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc)),
            ReceivedAtUtc = UtcInstant.Require(receivedAtUtc, nameof(receivedAtUtc)),
        };
    }

    public void MarkApplied(DateTimeOffset appliedAtUtc) => Close(AppliedResult, appliedAtUtc);

    public void MarkRejected(DateTimeOffset rejectedAtUtc) => Close(RejectedCallMismatchResult, rejectedAtUtc);

    private void Close(string result, DateTimeOffset at)
    {
        if (AppliedAtUtc is not null) return;
        Result = result;
        AppliedAtUtc = UtcInstant.Require(at, nameof(at));
    }
}
