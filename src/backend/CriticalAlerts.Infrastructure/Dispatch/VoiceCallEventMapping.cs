using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// The single mapping from the closed call-event vocabulary to attempt dimensions, shared by the callback intake and
/// the worker. Only a completed playback is delivery; anything that ends a call before that is a visible failure.
/// </summary>
internal static class VoiceCallEventMapping
{
    public static string Kind(VoiceCallEventKind kind) => kind switch
    {
        VoiceCallEventKind.Answered => "answered",
        VoiceCallEventKind.PlaybackCompleted => "playback-completed",
        VoiceCallEventKind.PlaybackFailed => "playback-failed",
        VoiceCallEventKind.Ended => "ended",
        _ => throw new DispatchValidationException("call-event-invalid", "The call event kind is not supported."),
    };

    public static string Reason(VoiceCallEndReason reason) => reason switch
    {
        VoiceCallEndReason.Completed => "completed",
        VoiceCallEndReason.NoAnswer => "no-answer",
        VoiceCallEndReason.Busy => "busy",
        VoiceCallEndReason.Declined => "declined",
        VoiceCallEndReason.Unreachable => "unreachable",
        VoiceCallEndReason.Failed => "failed",
        VoiceCallEndReason.HungUp => "hung-up",
        _ => throw new DispatchValidationException("call-event-invalid", "The call end reason is not supported."),
    };

    public static (string EventType, DeliveryAttemptStatus Status, string? FailureCategory) Map(string kind, string? endReason)
        => (kind, endReason) switch
        {
            ("answered", null) => ("call-answered", DeliveryAttemptStatus.Submitted, null),
            ("playback-completed", null) => ("delivered", DeliveryAttemptStatus.Delivered, null),
            ("playback-failed", null) => ("failed", DeliveryAttemptStatus.Failed, "voice-playback-failed"),
            ("ended", "no-answer") => ("failed", DeliveryAttemptStatus.Failed, "voice-no-answer"),
            ("ended", "busy") => ("failed", DeliveryAttemptStatus.Failed, "voice-busy"),
            ("ended", "declined") => ("failed", DeliveryAttemptStatus.Failed, "voice-declined"),
            ("ended", "unreachable" or "failed") => ("failed", DeliveryAttemptStatus.Failed, "voice-call-failed"),
            // A call that ends before the playback completed (hang-up or a normal end) never counts as delivery.
            ("ended", "hung-up" or "completed") => ("failed", DeliveryAttemptStatus.Failed, "voice-playback-incomplete"),
            _ => throw new DispatchValidationException("call-event-invalid", "The call event is not supported."),
        };

    public static string Metadata(string kind, string? endReason)
        => endReason is null ? $"voice-callback:{kind}" : $"voice-callback:{kind}:{endReason}";
}
