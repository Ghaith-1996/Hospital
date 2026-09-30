using System.Globalization;
using System.Text.Json;
using CriticalAlerts.Api.Authentication;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Dispatch;

namespace CriticalAlerts.Api.Http;

/// <summary>
/// Authenticated Azure Event Grid intake for ACS SMS delivery reports (Event Grid schema only).
/// Callbacks are untrusted: the whole batch is validated before any report is applied, and only opaque
/// identifiers, a closed status and UTC time leave this boundary. Numbers and provider text are never read.
/// </summary>
internal static class CommunicationWebhookEndpoints
{
    public const int MaxBodyBytes = 64 * 1024;
    public const int MaxEvents = 50;
    private const string ValidationEvent = "Microsoft.EventGrid.SubscriptionValidationEvent";
    private const string DeliveryReportEvent = "Microsoft.Communication.SMSDeliveryReportReceived";

    public static void MapCommunicationWebhookEndpoints(this WebApplication app, EventGridWebhookSettings settings)
    {
        if (!settings.Enabled) return;
        app.MapPost($"{ApiRouteConstants.BasePath}/webhooks/communications/acs-sms", Receive)
            .RequireAuthorization(EventGridWebhookSettings.Policy)
            .RequireRateLimiting("webhook")
            .ExcludeFromDescription();
    }

    private static async Task<IResult> Receive(
        HttpContext context,
        EventGridWebhookSettings settings,
        IProviderDeliveryReportService reports,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return Problem(StatusCodes.Status415UnsupportedMediaType, "content-type-unsupported");
        if (context.Request.ContentLength is > MaxBodyBytes)
            return Problem(StatusCodes.Status413PayloadTooLarge, "payload-too-large");

        var body = await ReadBoundedAsync(context.Request.Body, cancellationToken);
        if (body is null) return Problem(StatusCodes.Status413PayloadTooLarge, "payload-too-large");

        ParsedBatch batch;
        try
        {
            batch = Parse(body, settings, time.GetUtcNow());
        }
        catch (WebhookRejectedException rejected)
        {
            return Problem(StatusCodes.Status400BadRequest, rejected.Code);
        }
        catch (JsonException)
        {
            return Problem(StatusCodes.Status400BadRequest, "payload-invalid");
        }

        if (batch.ValidationCode is not null)
            return Results.Ok(new { validationResponse = batch.ValidationCode });

        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        var deferred = 0;
        foreach (var report in batch.Reports)
        {
            if (await reports.ApplyAsync(AzureCommunicationServicesSmsChannel.Provider, report, correlationId, cancellationToken)
                == ProviderDeliveryReportOutcome.Deferred)
                deferred++;
        }

        // Not acknowledging makes Event Grid redeliver the batch; reports already applied deduplicate.
        if (deferred > 0)
        {
            context.Response.Headers.RetryAfter = "30";
            return Problem(StatusCodes.Status503ServiceUnavailable, "delivery-report-deferred");
        }

        return Results.Ok(new { accepted = batch.Reports.Count });
    }

    private static ParsedBatch Parse(byte[] body, EventGridWebhookSettings settings, DateTimeOffset now)
    {
        using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() is 0 or > MaxEvents)
            throw new WebhookRejectedException("batch-invalid");

        var reports = new List<ProviderDeliveryReport>();
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new WebhookRejectedException("event-invalid");
            var id = RequiredString(item, "id");
            if (!IsSafeToken(id, 64) || !eventIds.Add(id)) throw new WebhookRejectedException("event-id-invalid");
            if (!item.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new WebhookRejectedException("event-data-invalid");

            switch (RequiredString(item, "eventType"))
            {
                case ValidationEvent:
                    // The handshake only echoes an opaque code after Entra authentication; it changes no state.
                    if (root.GetArrayLength() != 1) throw new WebhookRejectedException("batch-invalid");
                    var code = RequiredString(data, "validationCode");
                    if (!IsSafeToken(code, 128)) throw new WebhookRejectedException("validation-code-invalid");
                    return new ParsedBatch(code, []);
                case DeliveryReportEvent:
                    break;
                default:
                    throw new WebhookRejectedException("event-type-unsupported");
            }

            if (!string.Equals(RequiredString(item, "topic"), settings.ExpectedTopic, StringComparison.OrdinalIgnoreCase))
                throw new WebhookRejectedException("topic-unexpected");

            if (RequiredString(item, "dataVersion") != "1.0") throw new WebhookRejectedException("data-version-unsupported");
            if (!DateTimeOffset.TryParse(RequiredString(item, "eventTime"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var eventTime))
                throw new WebhookRejectedException("event-time-invalid");
            eventTime = eventTime.ToUniversalTime();
            if (eventTime < now.AddHours(-48) || eventTime > now.AddMinutes(5))
                throw new WebhookRejectedException("event-time-outside-window");

            var messageId = RequiredString(data, "messageId");
            if (!AzureCommunicationServicesSmsChannel.IsSafeMessageId(messageId)) throw new WebhookRejectedException("message-id-invalid");
            var status = RequiredString(data, "deliveryStatus") switch
            {
                "Delivered" => ProviderDeliveryReportStatus.Delivered,
                "Failed" => ProviderDeliveryReportStatus.Failed,
                _ => throw new WebhookRejectedException("delivery-status-unsupported"),
            };
            string? tag = null;
            foreach (var property in data.EnumerateObject())
            {
                if (!string.Equals(property.Name, "tag", StringComparison.OrdinalIgnoreCase)) continue;
                if (tag is not null || property.Value.ValueKind != JsonValueKind.String) throw new WebhookRejectedException("tag-invalid");
                tag = property.Value.GetString();
                if (!IsSafeToken(tag, 64)) throw new WebhookRejectedException("tag-invalid");
            }

            reports.Add(new ProviderDeliveryReport(id, messageId, tag, status, eventTime));
        }

        return new ParsedBatch(null, reports);
    }

    private static string RequiredString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new WebhookRejectedException("field-missing");

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

    private sealed record ParsedBatch(string? ValidationCode, IReadOnlyList<ProviderDeliveryReport> Reports);

    private sealed class WebhookRejectedException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
