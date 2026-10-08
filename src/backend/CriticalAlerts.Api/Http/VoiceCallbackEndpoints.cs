using System.Text.Json;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Dispatch;

namespace CriticalAlerts.Api.Http;

/// <summary>
/// Provider-neutral voice call callback intake. Callbacks are untrusted: the configured provider's reader authenticates
/// the request before any byte is parsed, the whole request is validated before any event is applied, and only opaque
/// identifiers, a closed kind and UTC time leave this boundary. Numbers and provider text are never read.
/// </summary>
internal static class VoiceCallbackEndpoints
{
    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxEvents = 50;

    public static void MapVoiceCallbackEndpoints(this WebApplication app, VoiceWebhookSettings settings)
    {
        if (!settings.Enabled) return;
        app.MapPost($"{ApiRouteConstants.BasePath}/webhooks/communications/voice/{{provider}}", Receive)
            .AllowAnonymous()
            .RequireRateLimiting("webhook")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Receive(
        string provider,
        HttpContext context,
        VoiceWebhookSettings settings,
        IEnumerable<IVoiceCallbackReader> readers,
        IProviderCallEventService callEvents,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider, settings.Provider, StringComparison.Ordinal)
            || VoiceWebhookRegistration.Resolve(readers, provider) is not { } reader)
            return Results.NotFound();
        if (context.Request.ContentLength is > MaxBodyBytes)
            return Problem(StatusCodes.Status413PayloadTooLarge, "payload-too-large");
        var body = await ReadBoundedAsync(context.Request.Body, cancellationToken);
        if (body is null) return Problem(StatusCodes.Status413PayloadTooLarge, "payload-too-large");

        if (!await reader.AuthenticateAsync(name => SingleHeader(context, name), body, cancellationToken))
            return Problem(StatusCodes.Status401Unauthorized, "callback-unauthenticated");
        if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return Problem(StatusCodes.Status415UnsupportedMediaType, "content-type-unsupported");

        IReadOnlyList<VoiceCallEvent> events;
        try
        {
            events = reader.Parse(body);
            Validate(events, time.GetUtcNow());
        }
        catch (VoiceCallbackRejectedException rejected)
        {
            return Problem(StatusCodes.Status400BadRequest, rejected.Code);
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "payload-invalid");
        }

        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        foreach (var callEvent in events)
            await callEvents.ApplyAsync(provider, callEvent, correlationId, cancellationToken);
        return Results.Ok(new { accepted = events.Count });
    }

    private static void Validate(IReadOnlyList<VoiceCallEvent> events, DateTimeOffset now)
    {
        if (events.Count is 0 or > MaxEvents) throw new VoiceCallbackRejectedException("batch-invalid");
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in events)
        {
            if (!IsSafeToken(item.EventId, 64) || !eventIds.Add(item.EventId)) throw new VoiceCallbackRejectedException("event-id-invalid");
            if (!ProviderVoiceChannel.IsSafeCallId(item.CallId)) throw new VoiceCallbackRejectedException("call-id-invalid");
            if (item.Tag is not null && !IsSafeToken(item.Tag, 64)) throw new VoiceCallbackRejectedException("tag-invalid");
            if (!Enum.IsDefined(item.Kind) || (item.EndReason is { } reason && !Enum.IsDefined(reason))
                || (item.Kind == VoiceCallEventKind.Ended) != (item.EndReason is not null))
                throw new VoiceCallbackRejectedException("event-kind-invalid");
            if (item.OccurredAtUtc.Offset != TimeSpan.Zero
                || item.OccurredAtUtc < now.AddHours(-48) || item.OccurredAtUtc > now.AddMinutes(5))
                throw new VoiceCallbackRejectedException("event-time-outside-window");
        }
    }

    private static string? SingleHeader(HttpContext context, string name)
        => context.Request.Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;

    private static bool IsSafeToken(string? value, int maximum)
        => !string.IsNullOrEmpty(value)
            && value.Length <= maximum
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxBodyBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) break;
            length += read;
        }

        return length > MaxBodyBytes ? null : buffer.AsSpan(0, length).ToArray();
    }

    private static IResult Problem(int status, string code)
        => Results.Problem(statusCode: status, title: "Webhook rejected", detail: code);
}
