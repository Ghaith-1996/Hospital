using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Dispatch;

namespace CriticalAlerts.Api.IntegrationTests;

/// <summary>How the fake voice provider answers the next create request.</summary>
public enum FakeVoiceMode
{
    Accept,

    /// <summary>Creates the call, then the reply is lost (500): the caller cannot know the call exists.</summary>
    AcceptThenLoseResponse,

    /// <summary>Creates the call, then replies 201 with a body of the wrong shape.</summary>
    AcceptWithMalformedBody,

    /// <summary>Creates the call, sends 201 headers, then never finishes the body.</summary>
    AcceptWithStalledBody,

    /// <summary>Never replies until the caller gives up.</summary>
    Hang,
    Redirect,
    Unauthorized,
    BadRequest,

    /// <summary>Documented non-execution: no call was created.</summary>
    Throttled,
}

public sealed record FakeVoiceCreate(
    bool SignatureValid,
    string RepeatabilityId,
    string FirstSent,
    string Tag,
    string To,
    string From,
    string Text,
    int Repeats,
    int RingTimeoutSeconds,
    string CallbackUri);

/// <summary>
/// Test-only reference voice provider endpoint. It verifies each create request's signature, executes the call script
/// at most once per repeatability ID when idempotent, and lets the test choose each reply. It never dials anything.
/// </summary>
public sealed class FakeVoiceServer(string sharedKey) : HttpMessageHandler
{
    public static readonly Uri BaseUri = new("https://sim-voice-provider.example.test/");
    private readonly ConcurrentQueue<FakeVoiceMode> modes = new();
    private readonly ConcurrentDictionary<string, string> callsByRepeatability = new(StringComparer.Ordinal);
    private int executed;
    private int nextCall;
    private CancellationTokenSource? crashAfterNextAccept;

    public bool Idempotent { get; set; } = true;

    public ConcurrentQueue<FakeVoiceCreate> RequestLog { get; } = new();

    public IReadOnlyList<FakeVoiceCreate> Requests => RequestLog.ToArray();

    public int ExecutedCalls => Volatile.Read(ref executed);

    /// <summary>Runs after the call is created and before the reply is returned (models a callback racing the reply).</summary>
    public Func<string, string, Task>? OnCallCreated { get; set; }

    public void Enqueue(FakeVoiceMode mode) => modes.Enqueue(mode);

    /// <summary>Cancels the worker right after the provider created the call, before the reply reaches it.</summary>
    public void CrashAfterNextAccept(CancellationTokenSource workerShutdown) => crashAfterNextAccept = workerShutdown;

    public static string Sign(string sharedKey, string repeatabilityId, string firstSent, byte[] body)
    {
        var material = $"POST\n/calls\n{repeatabilityId}\n{firstSent}\n{Convert.ToBase64String(SHA256.HashData(body))}";
        return Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(sharedKey), Encoding.UTF8.GetBytes(material)));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        var repeatability = Header(request, "Repeatability-Request-ID");
        var firstSent = Header(request, "Repeatability-First-Sent");
        var signatureValid = request.RequestUri == new Uri(BaseUri, "calls")
            && string.Equals(Header(request, "X-Sim-Signature"), Sign(sharedKey, repeatability, firstSent, body), StringComparison.Ordinal);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var tag = root.GetProperty("context").GetString()!;
        RequestLog.Enqueue(new FakeVoiceCreate(
            signatureValid,
            repeatability,
            firstSent,
            tag,
            root.GetProperty("to").GetString()!,
            root.GetProperty("from").GetString()!,
            root.GetProperty("text").GetString()!,
            root.GetProperty("repeats").GetInt32(),
            root.GetProperty("ringTimeoutSeconds").GetInt32(),
            root.GetProperty("callbackUri").GetString()!));

        switch (modes.TryDequeue(out var mode) ? mode : FakeVoiceMode.Accept)
        {
            case FakeVoiceMode.Unauthorized:
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            case FakeVoiceMode.BadRequest:
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            case FakeVoiceMode.Throttled:
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            case FakeVoiceMode.Redirect:
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://sim-elsewhere.example.test/calls");
                return redirect;
            case FakeVoiceMode.Hang:
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new OperationCanceledException(cancellationToken);
            case FakeVoiceMode.AcceptThenLoseResponse:
                await ExecuteAsync(repeatability, tag, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            case FakeVoiceMode.AcceptWithMalformedBody:
                await ExecuteAsync(repeatability, tag, cancellationToken);
                return Created("{\"callId\":42}");
            case FakeVoiceMode.AcceptWithStalledBody:
                await ExecuteAsync(repeatability, tag, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new StalledStream()) };
            default:
                var callId = await ExecuteAsync(repeatability, tag, cancellationToken);
                return Created(JsonSerializer.Serialize(new { callId }));
        }
    }

    private async Task<string> ExecuteAsync(string repeatability, string tag, CancellationToken cancellationToken)
    {
        if (Idempotent && callsByRepeatability.TryGetValue(repeatability, out var existing)) return existing;
        var callId = $"call-e2e-{Interlocked.Increment(ref nextCall):D4}";
        Interlocked.Increment(ref executed);
        if (Idempotent) callsByRepeatability[repeatability] = callId;
        if (OnCallCreated is { } created) await created(callId, tag);
        if (Interlocked.Exchange(ref crashAfterNextAccept, null) is { } shutdown)
        {
            await shutdown.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }

        return callId;
    }

    private static HttpResponseMessage Created(string json)
        => new(HttpStatusCode.Created) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Header(HttpRequestMessage request, string name)
        => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Test-only client for <see cref="FakeVoiceServer"/>; a real provider adapter has the same shape.</summary>
public sealed class ReferenceVoiceProvider(FakeVoiceServer server, string sharedKey, string accountIdentity = ReferenceVoiceProvider.DefaultAccount)
    : IVoiceCallProvider
{
    public const string ProviderName = "reference-fake";
    public const string DefaultAccount = "sim-voice-account-0001";
    private readonly HttpClient client = new(server, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    public string Name => ProviderName;

    public bool SupportsIdempotentCreate => server.Idempotent;

    public string AccountIdentity => accountIdentity;

    public async Task<VoiceCallCreateResult> CreateCallAsync(VoiceCallRequest request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            to = request.ToTestNumber,
            from = request.CallerId,
            text = request.SpokenText,
            repeats = request.Repeats,
            ringTimeoutSeconds = request.RingTimeoutSeconds,
            context = request.Tag,
            callbackUri = request.CallbackUri.ToString(),
        });
        var repeatability = request.RepeatabilityId.ToString("D");
        var firstSent = request.FirstSentAtUtc.ToString("r", CultureInfo.InvariantCulture);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(FakeVoiceServer.BaseUri, "calls")) { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        message.Headers.Add("Repeatability-Request-ID", repeatability);
        message.Headers.Add("Repeatability-First-Sent", firstSent);
        message.Headers.Add("X-Sim-Signature", FakeVoiceServer.Sign(sharedKey, repeatability, firstSent, body));
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        switch ((int)response.StatusCode)
        {
            case 201:
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                using (var json = JsonDocument.Parse(text))
                {
                    return json.RootElement.ValueKind == JsonValueKind.Object
                        && json.RootElement.TryGetProperty("callId", out var callId)
                        && callId.ValueKind == JsonValueKind.String
                            ? VoiceCallCreateResult.Accepted(callId.GetString()!)
                            : VoiceCallCreateResult.Ambiguous();
                }
            case 401 or 403:
                return VoiceCallCreateResult.AuthFailed();
            case 429:
                return VoiceCallCreateResult.NotExecuted();
            case 408 or >= 500 or (>= 300 and < 400):
                return VoiceCallCreateResult.Ambiguous();
            case >= 400 and < 500:
                return VoiceCallCreateResult.Rejected();
            default:
                return VoiceCallCreateResult.Ambiguous();
        }
    }
}

/// <summary>
/// Test-only callback reader for the reference provider: production <see cref="JwtCallbackAuthenticator"/> (RS256, issuer,
/// audience, lifetime) authenticates first; parsing maps the provider's words to the closed vocabulary.
/// </summary>
public sealed class ReferenceVoiceCallbackReader(JwtCallbackAuthenticator authenticator) : IVoiceCallbackReader
{
    public string Name => ReferenceVoiceProvider.ProviderName;

    public Task<bool> AuthenticateAsync(Func<string, string?> header, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        => authenticator.AuthenticateAsync(header("Authorization"), cancellationToken);

    public IReadOnlyList<VoiceCallEvent> Parse(ReadOnlyMemory<byte> body)
    {
        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        if (json.RootElement.ValueKind != JsonValueKind.Object
            || !json.RootElement.TryGetProperty("events", out var events)
            || events.ValueKind != JsonValueKind.Array)
            throw new VoiceCallbackRejectedException("batch-invalid");

        var parsed = new List<VoiceCallEvent>();
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new VoiceCallbackRejectedException("event-invalid");
            var kind = Required(item, "type") switch
            {
                "answered" => VoiceCallEventKind.Answered,
                "playback-completed" => VoiceCallEventKind.PlaybackCompleted,
                "playback-failed" => VoiceCallEventKind.PlaybackFailed,
                "ended" => VoiceCallEventKind.Ended,
                _ => throw new VoiceCallbackRejectedException("event-type-unsupported"),
            };
            VoiceCallEndReason? reason = item.TryGetProperty("reason", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() switch
                {
                    "completed" => VoiceCallEndReason.Completed,
                    "no-answer" => VoiceCallEndReason.NoAnswer,
                    "busy" => VoiceCallEndReason.Busy,
                    "declined" => VoiceCallEndReason.Declined,
                    "unreachable" => VoiceCallEndReason.Unreachable,
                    "failed" => VoiceCallEndReason.Failed,
                    "hung-up" => VoiceCallEndReason.HungUp,
                    _ => throw new VoiceCallbackRejectedException("end-reason-unsupported"),
                }
                : null;
            if (!DateTimeOffset.TryParse(Required(item, "time"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var occurred))
                throw new VoiceCallbackRejectedException("event-time-invalid");
            var tag = item.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.String ? context.GetString() : null;
            parsed.Add(new VoiceCallEvent(Required(item, "id"), Required(item, "callId"), tag, kind, reason, occurred.ToUniversalTime()));
        }

        return parsed;
    }

    private static string Required(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new VoiceCallbackRejectedException("field-missing");
}
