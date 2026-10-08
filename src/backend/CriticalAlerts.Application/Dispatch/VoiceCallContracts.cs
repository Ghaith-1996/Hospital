namespace CriticalAlerts.Application.Dispatch;

/// <summary>
/// A concrete voice provider behind the provider-neutral voice channel. It only places a call that speaks the given
/// generic text and hangs up; the channel owns validation, test-number mapping, the send ledger and every window.
/// Implementations may throw on transport failure: the channel treats any failure after the send began as ambiguous.
/// </summary>
public interface IVoiceCallProvider
{
    /// <summary>Closed, configured provider name (lowercase letters, digits and hyphens).</summary>
    string Name { get; }

    /// <summary>True only when the provider documents that a repeated create with the same repeatability ID executes once.</summary>
    bool SupportsIdempotentCreate { get; }

    /// <summary>Non-secret identity of the configured provider account, bound into the replay fingerprint.</summary>
    string AccountIdentity { get; }

    Task<VoiceCallCreateResult> CreateCallAsync(VoiceCallRequest request, CancellationToken cancellationToken);
}

/// <summary>The provider-executed call script: speak <paramref name="SpokenText"/> <paramref name="Repeats"/> times, then hang up.</summary>
public sealed record VoiceCallRequest(
    string ToTestNumber,
    string CallerId,
    string SpokenText,
    int Repeats,
    int RingTimeoutSeconds,
    string Tag,
    Guid RepeatabilityId,
    DateTimeOffset FirstSentAtUtc,
    Uri CallbackUri);

public enum VoiceCallCreateOutcome
{
    /// <summary>The provider created the call and returned its call ID.</summary>
    Accepted,

    /// <summary>The provider definitively refused this request (not retried).</summary>
    Rejected,

    /// <summary>The provider refused the credentials (not retried; configuration incident).</summary>
    AuthFailed,

    /// <summary>The provider documents that the request was not executed (for example throttling).</summary>
    NotExecuted,

    /// <summary>The call may or may not have been created.</summary>
    Ambiguous,
}

public sealed record VoiceCallCreateResult(VoiceCallCreateOutcome Outcome, string? CallId = null, DateTimeOffset? RetryAtUtc = null)
{
    public static VoiceCallCreateResult Accepted(string callId) => new(VoiceCallCreateOutcome.Accepted, callId);

    public static VoiceCallCreateResult Rejected() => new(VoiceCallCreateOutcome.Rejected);

    public static VoiceCallCreateResult AuthFailed() => new(VoiceCallCreateOutcome.AuthFailed);

    public static VoiceCallCreateResult NotExecuted(DateTimeOffset? retryAtUtc = null) => new(VoiceCallCreateOutcome.NotExecuted, RetryAtUtc: retryAtUtc);

    public static VoiceCallCreateResult Ambiguous() => new(VoiceCallCreateOutcome.Ambiguous);
}

/// <summary>
/// Authenticates and parses one provider's call callbacks. Authentication runs before any byte of the body is parsed.
/// Parsing returns only opaque identifiers, a closed kind and a UTC time; numbers and provider text are never modelled.
/// </summary>
public interface IVoiceCallbackReader
{
    string Name { get; }

    Task<bool> AuthenticateAsync(Func<string, string?> header, ReadOnlyMemory<byte> body, CancellationToken cancellationToken);

    /// <summary>Throws <see cref="VoiceCallbackRejectedException"/> for any malformed event; the whole request is rejected.</summary>
    IReadOnlyList<VoiceCallEvent> Parse(ReadOnlyMemory<byte> body);
}

public sealed record VoiceCallEvent(
    string EventId,
    string CallId,
    string? Tag,
    VoiceCallEventKind Kind,
    VoiceCallEndReason? EndReason,
    DateTimeOffset OccurredAtUtc);

public enum VoiceCallEventKind
{
    Answered,
    PlaybackCompleted,
    PlaybackFailed,
    Ended,
}

public enum VoiceCallEndReason
{
    Completed,
    NoAnswer,
    Busy,
    Declined,
    Unreachable,
    Failed,
    HungUp,
}

public sealed class VoiceCallbackRejectedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface IProviderCallEventService
{
    Task<ProviderCallEventOutcome> ApplyAsync(
        string provider,
        VoiceCallEvent callEvent,
        string correlationId,
        CancellationToken cancellationToken);
}

public enum ProviderCallEventOutcome
{
    Applied,
    NoStateChange,
    Duplicate,
    Unmatched,
    TagMismatch,

    /// <summary>The call ID is not committed yet but the tag matches a committed send: held until the worker applies it.</summary>
    Pending,
}
