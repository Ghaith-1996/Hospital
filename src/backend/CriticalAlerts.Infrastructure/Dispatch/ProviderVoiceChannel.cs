using System.Security.Cryptography;
using System.Text;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Provider-neutral, test-number-only voice channel. It owns validation, test-number mapping, the replay binding, the
/// time windows and the state mapping; a concrete <see cref="IVoiceCallProvider"/> only places the call. A provider
/// accepting the call is recorded as Submitted only, and only a completed playback is Delivered. A provider that cannot
/// deduplicate creates is never asked twice for the same attempt. No number, key or spoken text is logged or returned.
/// </summary>
public sealed class ProviderVoiceChannel : INotificationChannel
{
    public const string Uncertain = "provider-outcome-uncertain";

    /// <summary>Whole-request bound (headers and body). The worker holds the alert lock while this runs.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly VoiceDispatchOptions options;
    private readonly IVoiceCallProvider provider;
    private readonly CriticalAlertsDbContext db;
    private readonly TimeProvider time;
    private readonly TimeSpan requestTimeout;

    public ProviderVoiceChannel(
        VoiceDispatchOptions options,
        IVoiceCallProvider provider,
        CriticalAlertsDbContext db,
        TimeProvider time,
        TimeSpan? requestTimeout = null)
    {
        if (!string.Equals(provider.Name, options.ProviderName, StringComparison.Ordinal))
            throw new InvalidOperationException("The voice provider does not match the configured provider name.");
        this.options = options;
        this.provider = provider;
        this.db = db;
        this.time = time;
        this.requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }

    public NotificationChannel ChannelType => NotificationChannel.Voice;

    public string ProviderName => provider.Name;

    public bool RequiresDurableFirstSend => true;

    /// <summary>Opaque per-attempt tag echoed by provider callbacks; derived from the attempt key, bound in the send ledger.</summary>
    public static string CreateTag(string providerName, string attemptIdempotencyKey)
        => "ca-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{providerName}|voice-tag|{attemptIdempotencyKey}")))[..32];

    public static Guid CreateRepeatabilityId(string providerName, string attemptIdempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{providerName}|voice-repeatability|{attemptIdempotencyKey}"))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    public static bool IsSafeCallId(string? value)
        => !string.IsNullOrEmpty(value)
            && value.Length <= 100
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public ProviderSendBinding? DescribeSend(NotificationDispatchRequest request)
        => new(CreateTag(provider.Name, request.IdempotencyKey), Fingerprint(request));

    public async Task<NotificationDispatchResult> DispatchAsync(
        NotificationDispatchRequest request,
        SimulationDispatchScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        var now = time.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
            throw new DispatchValidationException("clock-not-utc", "Provider dispatch requires a UTC clock.");

        var tag = CreateTag(provider.Name, request.IdempotencyKey);

        // Status first: a placed call only waits for its call events and needs no recipient lookup, so a mapping removed
        // or renamed after the call cannot falsely fail it.
        switch (request.CurrentAttemptStatus)
        {
            case DeliveryAttemptStatus.Submitted:
                return await AwaitOutcomeAsync(request, tag, now, cancellationToken);
            case DeliveryAttemptStatus.Delivered or DeliveryAttemptStatus.Failed:
                return new NotificationDispatchResult(string.Empty, [], Retryable: false, FailureCategory: null);
        }

        var requestedAt = request.AttemptRequestedAtUtc
            ?? throw new DispatchValidationException("request-invalid", "Provider dispatch requires the durable attempt time.");
        var firstSent = request.FirstSentAtUtc ?? requestedAt;
        if (!options.TestRecipients.TryGetValue(request.EndpointReference, out var testNumber))
            return Failed(tag, "test-recipient-not-configured", now, retryable: false);
        // A replay must be the same call: changed provider account, caller ID, test number or text is a different call.
        if (request.RecordedSendFingerprint is { } recorded
            && !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(recorded), Encoding.ASCII.GetBytes(Fingerprint(request))))
            return Failed(tag, Uncertain, now, retryable: false);
        // Without documented create deduplication, any earlier invocation may have placed the call: never ask again.
        if (!request.FirstProviderInvocation && !provider.SupportsIdempotentCreate)
            return Failed(tag, Uncertain, now, retryable: false);
        if (now - firstSent >= options.UncertainOutcomeWindow || now - requestedAt >= options.UncertainOutcomeWindow)
            return Failed(tag, Uncertain, now, retryable: false);

        VoiceCallCreateResult result;
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            deadline.CancelAfter(requestTimeout);
            try
            {
                result = await provider.CreateCallAsync(new VoiceCallRequest(
                    testNumber,
                    options.CallerId,
                    request.WakeUpText,
                    options.Repeats,
                    options.RingTimeoutSeconds,
                    tag,
                    CreateRepeatabilityId(provider.Name, request.IdempotencyKey),
                    firstSent,
                    options.CallbackUri), deadline.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Timeout, transport failure or an unreadable reply after the send began: the call may exist.
                result = VoiceCallCreateResult.Ambiguous();
            }
        }

        switch (result.Outcome)
        {
            case VoiceCallCreateOutcome.Accepted when IsSafeCallId(result.CallId):
                var events = new List<NotificationProviderEvent>
                {
                    new($"{EventPrefix}:{tag}:submitted", "submitted", now, "voice-provider:accepted"),
                };
                events.AddRange(await TakePendingEventsAsync(tag, result.CallId!, now, cancellationToken));
                return new NotificationDispatchResult(result.CallId!, events, Retryable: false, FailureCategory: null);
            case VoiceCallCreateOutcome.Rejected:
                return Failed(tag, "voice-call-rejected", now, retryable: false);
            case VoiceCallCreateOutcome.AuthFailed:
                return Failed(tag, "provider-auth-failed", now, retryable: false);
            case VoiceCallCreateOutcome.NotExecuted when request.FirstProviderInvocation:
                // Documented non-execution of the only invocation of this key: a new attempt (and key) is safe.
                return Failed(tag, "provider-unavailable", now, retryable: true, result.RetryAtUtc);
            default:
                // Ambiguous, an unsafe call ID, or a refused replay (which does not prove an earlier invocation was not executed).
                return provider.SupportsIdempotentCreate
                    ? new NotificationDispatchResult(string.Empty, [], Retryable: true, FailureCategory: Uncertain, RetryAtUtc: now.AddSeconds(5))
                    : Failed(tag, Uncertain, now, retryable: false);
        }
    }

    private string EventPrefix => $"voice:{provider.Name}";

    private async Task<NotificationDispatchResult> AwaitOutcomeAsync(
        NotificationDispatchRequest request,
        string tag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var reference = request.CurrentProviderReference ?? string.Empty;
        var events = await TakePendingEventsAsync(tag, reference, now, cancellationToken);
        var terminal = events.Any(item => item.EventType is "delivered" or "failed");
        var submittedAt = request.SubmittedAtUtc ?? request.AttemptRequestedAtUtc ?? now;
        if (!terminal && now - submittedAt >= options.CallOutcomeWindow)
        {
            events.Add(FailedEvent(tag, "call-outcome-unconfirmed", now));
            terminal = true;
        }

        return terminal
            ? new NotificationDispatchResult(reference, events, Retryable: false, FailureCategory: null)
            : new NotificationDispatchResult(reference, events, Retryable: true, FailureCategory: null,
                RetryAtUtc: Min(now.AddSeconds(5), submittedAt + options.CallOutcomeWindow));
    }

    /// <summary>
    /// Call events that arrived before the call ID was committed, in occurrence order. They are applied in this worker
    /// transaction; an event for a different call ID is rejected without a state change.
    /// </summary>
    private async Task<List<NotificationProviderEvent>> TakePendingEventsAsync(
        string tag,
        string callId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = await db.PendingProviderCallEvents
            .Where(row => row.Provider == provider.Name && row.CallbackTag == tag && row.AppliedAtUtc == null)
            .OrderBy(row => row.OccurredAtUtc)
            .ThenBy(row => row.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        var events = new List<NotificationProviderEvent>();
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(callId) || !string.Equals(row.CallId, callId, StringComparison.Ordinal))
            {
                row.MarkRejected(now);
                continue;
            }

            var (eventType, _, category) = VoiceCallEventMapping.Map(row.Kind, row.EndReason);
            events.Add(new NotificationProviderEvent(
                ProviderCallEventService.ProviderEventId(provider.Name, row.ExternalEventId),
                eventType,
                row.OccurredAtUtc,
                VoiceCallEventMapping.Metadata(row.Kind, row.EndReason),
                category));
            row.MarkApplied(now);
        }

        return events;
    }

    private string Fingerprint(NotificationDispatchRequest request)
    {
        var number = options.TestRecipients.GetValueOrDefault(request.EndpointReference) ?? "unmapped";
        var material = string.Join('\n', provider.Name, provider.AccountIdentity, options.CallerId, number,
            request.WakeUpText, options.Repeats.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private NotificationDispatchResult Failed(string tag, string category, DateTimeOffset now, bool retryable, DateTimeOffset? retryAt = null)
        => new(
            string.Empty,
            [FailedEvent(tag, category, now)],
            Retryable: retryable,
            FailureCategory: category,
            RetryAtUtc: retryable ? retryAt ?? now.AddSeconds(5) : null);

    private NotificationProviderEvent FailedEvent(string tag, string category, DateTimeOffset now)
        => new($"{EventPrefix}:{tag}:failed", "failed", now, $"voice:{category}", category);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    private static void Validate(NotificationDispatchRequest request)
    {
        if (request.Channel != NotificationChannel.Voice
            || request.OrganizationId.Value == Guid.Empty
            || request.AlertId.Value == Guid.Empty
            || request.RecipientSelectionId.Value == Guid.Empty
            || request.DraftVersion.Value <= 0
            || !IsSafeReference(request.EndpointReference, "SIM-")
            || !IsSafeReference(request.IdempotencyKey, "alert-dispatch:")
            || !IsSafeReference(request.CorrelationId, "dispatch:")
            || (request.AttemptRequestedAtUtc is { } requested && requested.Offset != TimeSpan.Zero)
            || (request.SubmittedAtUtc is { } submitted && submitted.Offset != TimeSpan.Zero)
            || (request.FirstSentAtUtc is { } firstSent && firstSent.Offset != TimeSpan.Zero))
        {
            throw new DispatchValidationException("request-invalid", "The provider dispatch request is invalid.");
        }

        var text = request.WakeUpText;
        if (string.IsNullOrWhiteSpace(text)
            || !text.StartsWith("SIMULATION:", StringComparison.Ordinal)
            || text.Length > 200
            || text.Any(char.IsControl))
        {
            throw new DispatchValidationException("wake-up-text-invalid", "Voice wake-up text must be generic synthetic content.");
        }
    }

    private static bool IsSafeReference(string value, string prefix)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 200
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_');
}
