using System.Security.Claims;
using System.Security.Cryptography;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Assistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

namespace CriticalAlerts.Api.Http;

internal static class AssistanceEndpoints
{
    public static void MapAssistanceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRouteConstants.BasePath).RequireAuthorization(AuthorizationPolicies.AlertDraftEditor).RequireRateLimiting("api");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (AssistanceException failure) { return Results.Problem(statusCode: failure.Status, title: "Assistance unavailable", detail: failure.Code); }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 409, title: "Draft changed", detail: "draft-version-stale"); }
            catch (DomainException) { return Results.Problem(statusCode: 409, title: "Draft cannot be changed", detail: "alert-not-editable"); }
            catch (BadHttpRequestException failure) when (failure.StatusCode == 413) { return Results.Problem(statusCode: 413, title: "Audio exceeds the request limit"); }
            catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) { return Results.Problem(statusCode: 503, title: "Assistance unavailable", detail: "Continue with manual editing."); }
        });
        group.MapGet("/capabilities", (AssistanceSettings settings) => Results.Ok(settings.Capabilities))
            .Produces<AssistanceCapabilities>().WithApiErrors(503);
        group.MapPost("/alerts/{alertId:guid}/transcriptions", Transcribe).WithIdempotencyHeader(100)
            .Produces<AssistanceResultView>().WithApiErrors(400, 404, 409, 413, 415, 503)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters ??= [];
                foreach (var name in new[] { "X-Alert-Draft-Version", "X-Audio-Language-Hint", "X-Simulation-Scenario" })
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = name,
                        In = ParameterLocation.Header,
                        Required = name == "X-Alert-Draft-Version",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                    });
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["audio/wav"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                        ["audio/webm"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                        ["audio/ogg"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                        ["audio/mp4"] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
                    }
                };
                operation.Description = "Bounded raw audio, no persistence. Accepted formats come from capabilities. Simulation scenarios are Development/Test-only.";
                return Task.CompletedTask;
            });
        group.MapPost("/alerts/{alertId:guid}/structuring-suggestions", Structure).WithIdempotencyHeader(100)
            .Produces<AssistanceResultView>().WithApiErrors(400, 404, 409, 413, 503);
        foreach (var (path, kind) in new[] { ("transcriptions", AssistanceKind.Transcription), ("structuring-suggestions", AssistanceKind.Structuring) })
        {
            group.MapGet($"/alerts/{{alertId:guid}}/{path}", (Guid alertId, string? cursor, HttpContext http, IAssistanceService service, CancellationToken token) =>
                service.GetAsync(Organization(http), new(alertId), kind, cursor, token)).Produces<AssistancePage>().WithApiErrors(400, 404, 503);
            group.MapPost($"/alerts/{{alertId:guid}}/{path}/{{resultId:guid}}/apply",
                (Guid alertId, Guid resultId, AssistanceRequest request, HttpContext http, IAssistanceService service, CancellationToken token) =>
                    service.ApplyAsync(Organization(http), Actor(http), Correlation(http), new(alertId), kind, resultId,
                        request.ExpectedVersion, Key(http), token)).WithIdempotencyHeader(100)
                .Produces<AssistanceApplyResult>().WithApiErrors(400, 404, 409, 413, 503);
        }
    }
    private static Task<AssistanceResultView> Structure(Guid alertId, AssistanceRequest request, HttpContext http,
        IAssistanceService service, CancellationToken token) => service.GenerateAsync(Organization(http), Actor(http), Correlation(http),
            new(alertId), AssistanceKind.Structuring, request.ExpectedVersion, Key(http), null, null, new(null), token);

    private static async Task<AssistanceResultView> Transcribe(Guid alertId, HttpContext http, IAssistanceService service,
        AssistanceSettings settings, CancellationToken token)
    {
        if (!int.TryParse(http.Request.Headers["X-Alert-Draft-Version"], out var version) || version < 1)
            throw new AssistanceException("draft-version-required");
        var contentType = http.Request.ContentType?.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        if (!settings.Capabilities.SpeechTranscription)
            throw new AssistanceException(settings.SpeechRequested ? "provider-unavailable" : "feature-disabled", settings.SpeechRequested ? 503 : 409);
        if (!settings.Capabilities.AcceptedAudioContentTypes.Contains(contentType)) throw new AssistanceException("audio-type-unsupported", 415);
        var buffer = new byte[ApiLimits.MaxRequestBodyBytes + 1];
        byte[]? audio = null;
        try
        {
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await http.Request.Body.ReadAsync(buffer.AsMemory(length), token);
                if (read == 0) break;
                length += read;
            }
            if (length > ApiLimits.MaxRequestBodyBytes) throw new AssistanceException("audio-too-large", 413);
            if (length == 0) throw new AssistanceException("audio-empty");
            audio = buffer.AsSpan(0, length).ToArray();
            var language = http.Request.Headers["X-Audio-Language-Hint"].ToString();
            var scenario = http.Request.Headers["X-Simulation-Scenario"].ToString();
            return await service.GenerateAsync(Organization(http), Actor(http), Correlation(http), new(alertId), AssistanceKind.Transcription,
                version, Key(http), audio, contentType, new(language.Length == 0 ? null : language, scenario.Length == 0 ? null : scenario), token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            if (audio is not null) CryptographicOperations.ZeroMemory(audio);
        }
    }
    private static OrganizationId Organization(HttpContext http) => new(Guid.Parse(http.User.FindFirstValue(AuthenticationClaimTypes.OrganizationId)!));
    private static UserId Actor(HttpContext http) => new(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
    private static string Correlation(HttpContext http) => http.Response.Headers["X-Correlation-ID"].ToString();
    private static string Key(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();
}
