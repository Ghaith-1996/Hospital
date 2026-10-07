using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Test-number-only Azure Communication Services SMS adapter (ACS SMS REST 2021-03-07, HMAC access key).
/// Sends only the generic policy wake-up text, records provider acceptance as Submitted only, and leaves
/// Delivered to authenticated delivery reports. No payload, number, key or provider text is logged or returned.
/// </summary>
public sealed class AzureCommunicationServicesSmsChannel : INotificationChannel, IDisposable
{
    public const string Provider = "azure-communication-services-sms";
    public const string ApiPathAndQuery = "/sms?api-version=2021-03-07";
    private const int MaxResponseBytes = 64 * 1024;
    private const string Uncertain = "provider-outcome-uncertain";

    /// <summary>Whole-request bound (headers and body). The worker holds the alert lock while this runs.</summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(10);

    private readonly AcsSmsOptions options;
    private readonly HttpClient client;
    private readonly TimeProvider time;
    private readonly TimeSpan requestTimeout;

    public AzureCommunicationServicesSmsChannel(AcsSmsOptions options, HttpMessageHandler handler, TimeProvider time, TimeSpan? requestTimeout = null)
    {
        this.options = options;
        this.time = time;
        this.requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        // The linked per-request token is authoritative; HttpClient.Timeout stops applying after the headers.
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public NotificationChannel ChannelType => NotificationChannel.Sms;

    public string ProviderName => Provider;

    public static HttpMessageHandler CreateDefaultHandler()
        => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

    /// <summary>Opaque per-attempt tag echoed by ACS delivery reports; derived, never stored separately.</summary>
    public static string CreateTag(string attemptIdempotencyKey)
        => "ca-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Provider}|tag|{attemptIdempotencyKey}")))[..32];

    public static Guid CreateRepeatabilityRequestId(string attemptIdempotencyKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Provider}|repeatability|{attemptIdempotencyKey}"))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

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

        var tag = CreateTag(request.IdempotencyKey);

        // Status first: an attempt ACS already accepted only waits for its report, needing no recipient lookup,
        // so a mapping removed or renamed after the send cannot falsely fail it.
        switch (request.CurrentAttemptStatus)
        {
            case DeliveryAttemptStatus.Submitted:
                var submittedAt = request.SubmittedAtUtc ?? request.AttemptRequestedAtUtc ?? now;
                return now - submittedAt >= options.DeliveryReportWindow
                    ? Failed(tag, "delivery-unconfirmed", now, retryable: false)
                    : new NotificationDispatchResult(string.Empty, [], Retryable: true, FailureCategory: null,
                        RetryAtUtc: Min(now.AddSeconds(5), submittedAt + options.DeliveryReportWindow));
            case DeliveryAttemptStatus.Delivered or DeliveryAttemptStatus.Failed:
                return new NotificationDispatchResult(string.Empty, [], Retryable: false, FailureCategory: null);
        }

        var requestedAt = request.AttemptRequestedAtUtc
            ?? throw new DispatchValidationException("request-invalid", "Provider dispatch requires the durable attempt time.");
        if (now - requestedAt >= options.UncertainOutcomeWindow)
            return Failed(tag, Uncertain, now, retryable: false);
        if (!options.TestRecipients.TryGetValue(request.EndpointReference, out var testNumber))
            return Failed(tag, "test-recipient-not-configured", now, retryable: false);

        // repeatabilityFirstSent must be identical for every replay of this attempt. The attempt row (and its
        // RequestedAtUtc) can be rolled back if the worker dies after ACS accepted the send, so the worker supplies
        // a time that was durable before any provider call (the outbox row's creation time).
        var firstSent = request.FirstSentAtUtc ?? requestedAt;
        byte[]? body = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(requestTimeout);
        try
        {
            body = JsonSerializer.SerializeToUtf8Bytes(new SendRequest(
                options.FromNumber,
                [new SendRecipient(testNumber, CreateRepeatabilityRequestId(request.IdempotencyKey).ToString("D"),
                    firstSent.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture))],
                request.WakeUpText,
                new SendOptions(true, tag)));
            using var message = Sign(body, now);
            // One deadline bounds the headers and the streamed body; expiry is an ambiguous outcome.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return await MapAsync(response, tag, now, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            return UncertainResult(now);
        }
        finally
        {
            if (body is not null) CryptographicOperations.ZeroMemory(body);
        }
    }

    public void Dispose() => client.Dispose();

    private HttpRequestMessage Sign(byte[] body, DateTimeOffset now)
    {
        var uri = new Uri(options.Endpoint, ApiPathAndQuery);
        var date = now.ToString("r", CultureInfo.InvariantCulture);
        var contentHash = Convert.ToBase64String(SHA256.HashData(body));
        var stringToSign = $"POST\n{uri.PathAndQuery}\n{date};{uri.Authority};{contentHash}";
        var signature = Convert.ToBase64String(HMACSHA256.HashData(options.AccessKey, Encoding.UTF8.GetBytes(stringToSign)));
        var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Add("x-ms-date", date);
        message.Headers.Add("x-ms-content-sha256", contentHash);
        message.Headers.TryAddWithoutValidation("Authorization",
            $"HMAC-SHA256 SignedHeaders=x-ms-date;host;x-ms-content-sha256&Signature={signature}");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private static async Task<NotificationDispatchResult> MapAsync(
        HttpResponseMessage response,
        string tag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                return Failed(tag, "provider-auth-failed", now, retryable: false);
            case HttpStatusCode.TooManyRequests:
                return Failed(tag, "provider-unavailable", now, retryable: true);
            case HttpStatusCode.RequestTimeout:
                return UncertainResult(now);
            case HttpStatusCode.Accepted:
                break;
            case var status when (int)status is >= 400 and < 500:
                return Failed(tag, "sms-rejected", now, retryable: false);
            default:
                return UncertainResult(now);
        }

        byte[]? content = null;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            content = await ReadBoundedAsync(stream, cancellationToken);
            if (content is null) return UncertainResult(now);
            using var json = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8 });
            // Any shape other than the documented one is ambiguous: ACS may have accepted the send.
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array
                || items.GetArrayLength() != 1
                || items[0].ValueKind != JsonValueKind.Object)
                return UncertainResult(now);

            var item = items[0];
            if (!TryGetOptional(item, "successful", JsonValueKind.True, JsonValueKind.False, out var success)
                || !TryGetOptional(item, "httpStatusCode", JsonValueKind.Number, JsonValueKind.Number, out var code)
                || !TryGetOptional(item, "repeatabilityResult", JsonValueKind.String, JsonValueKind.String, out var repeat)
                || !TryGetOptional(item, "messageId", JsonValueKind.String, JsonValueKind.String, out var id))
                return UncertainResult(now);

            var successful = success?.ValueKind == JsonValueKind.True;
            var itemStatus = code is { } number && number.TryGetInt32(out var parsed) ? parsed : 0;
            var repeatability = repeat?.GetString();
            var messageId = id?.GetString();

            if (string.Equals(repeatability, "rejected", StringComparison.OrdinalIgnoreCase))
                return Failed(tag, "provider-repeatability-rejected", now, retryable: false);
            if (successful)
            {
                if (!IsSafeMessageId(messageId)) return UncertainResult(now);
                return new NotificationDispatchResult(
                    messageId!,
                    [new NotificationProviderEvent($"acs-sms:{tag}:submitted", "submitted", now, "acs-sms:accepted")],
                    Retryable: false,
                    FailureCategory: null);
            }

            if (messageId is not null) return UncertainResult(now);
            return itemStatus is 429 or >= 500
                ? Failed(tag, "provider-unavailable", now, retryable: true)
                : itemStatus is >= 400 and < 500
                    ? Failed(tag, "sms-rejected", now, retryable: false)
                    : UncertainResult(now);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return UncertainResult(now);
        }
        finally
        {
            if (content is not null) CryptographicOperations.ZeroMemory(content);
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxResponseBytes + 1];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }

            return length is 0 or > MaxResponseBytes ? null : buffer.AsSpan(0, length).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static NotificationDispatchResult Failed(string tag, string category, DateTimeOffset now, bool retryable)
        => new(
            string.Empty,
            [new NotificationProviderEvent($"acs-sms:{tag}:failed", "failed", now, $"acs-sms:{category}", category)],
            Retryable: retryable,
            FailureCategory: category,
            RetryAtUtc: retryable ? now.AddSeconds(5) : null);

    private static NotificationDispatchResult UncertainResult(DateTimeOffset now)
        => new(string.Empty, [], Retryable: true, FailureCategory: Uncertain, RetryAtUtc: now.AddSeconds(5));

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;

    /// <summary>An absent or null property is allowed; a present property must have one of the expected kinds.</summary>
    private static bool TryGetOptional(JsonElement item, string name, JsonValueKind first, JsonValueKind second, out JsonElement? value)
    {
        value = null;
        if (!item.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return true;
        if (property.ValueKind != first && property.ValueKind != second) return false;
        value = property;
        return true;
    }

    public static bool IsSafeMessageId(string? value)
        => !string.IsNullOrEmpty(value)
            && value.Length <= 100
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static void Validate(NotificationDispatchRequest request)
    {
        if (request.Channel != NotificationChannel.Sms
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
            || text.Length > 160
            || text.Any(character => character is < ' ' or > '~'))
        {
            throw new DispatchValidationException("wake-up-text-invalid", "SMS wake-up text must be generic single-segment synthetic content.");
        }
    }

    private static bool IsSafeReference(string value, string prefix)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 200
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_');

    private sealed record SendRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("from")] string From,
        [property: System.Text.Json.Serialization.JsonPropertyName("smsRecipients")] SendRecipient[] SmsRecipients,
        [property: System.Text.Json.Serialization.JsonPropertyName("message")] string Message,
        [property: System.Text.Json.Serialization.JsonPropertyName("smsSendOptions")] SendOptions SmsSendOptions);

    private sealed record SendRecipient(
        [property: System.Text.Json.Serialization.JsonPropertyName("to")] string To,
        [property: System.Text.Json.Serialization.JsonPropertyName("repeatabilityRequestId")] string RepeatabilityRequestId,
        [property: System.Text.Json.Serialization.JsonPropertyName("repeatabilityFirstSent")] string RepeatabilityFirstSent);

    private sealed record SendOptions(
        [property: System.Text.Json.Serialization.JsonPropertyName("enableDeliveryReport")] bool EnableDeliveryReport,
        [property: System.Text.Json.Serialization.JsonPropertyName("tag")] string Tag);
}
