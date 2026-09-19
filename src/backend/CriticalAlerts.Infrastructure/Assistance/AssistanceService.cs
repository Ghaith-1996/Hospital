using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Assistance;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Assistance;

public sealed class AssistanceService(CriticalAlertsDbContext db, ISensitiveDataProtector protector, TimeProvider time,
    AssistanceSettings settings, ITranscriptionProvider transcription, IAlertStructuringProvider structuring) : IAssistanceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AssistanceResultView> GenerateAsync(OrganizationId organization, UserId actor, string correlation, AlertId alertId,
        AssistanceKind kind, int expectedVersion, string? key, byte[]? audio, string? contentType,
        TranscriptionOptions options, CancellationToken cancellationToken)
    {
        EnsureFeature(kind);
        var operation = Prefix(kind) + ".generate";
        key = RequireKey(key);
        if (!AssistanceValidation.LanguageAllowed(options.LanguageHint)) throw new AssistanceException("language-invalid");
        if (kind == AssistanceKind.Transcription)
        {
            if (audio is not { Length: > 0 }) throw new AssistanceException("audio-empty");
            if (audio.Length > AssistanceSettings.MaxAudioBytes) throw new AssistanceException("audio-too-large", 413);
            if (!settings.Capabilities.AcceptedAudioContentTypes.Contains(contentType)) throw new AssistanceException("audio-type-unsupported", 415);
            if (settings.Capabilities.SpeechProvider != "Simulated" && options.SimulationScenario is not null)
                throw new AssistanceException("simulation-scenario-invalid");
        }
        var hash = Hash(organization.Value, actor.Value, alertId.Value, kind, expectedVersion,
            kind == AssistanceKind.Transcription ? Convert.ToHexString(SHA256.HashData(audio!)) : null,
            contentType, options.LanguageHint, options.SimulationScenario,
            kind == AssistanceKind.Transcription ? settings.Capabilities.SpeechProvider : "Simulated",
            kind == AssistanceKind.Transcription ? settings.AzureResourceName : null, AssistanceSettings.ConfigurationVersion);
        AlertSourceRevision source;
        IdempotencyRecord claim;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await AlertMutationLock.AcquireAsync(db, organization, alertId, cancellationToken);
            var alert = await Load(organization, alertId, cancellationToken);
            var existing = await FindClaim(organization, operation, key, cancellationToken);
            if (existing is not null)
            {
                CheckHash(existing, hash);
                var resultId = CompletedResult(existing);
                await transaction.CommitAsync(cancellationToken);
                return await View(await Result(organization, alertId, kind, resultId, cancellationToken), cancellationToken);
            }
            EnsureEditable(alert, expectedVersion);
            source = alert.CurrentSourceRevision ?? throw new AssistanceException("source-required");
            claim = IdempotencyRecord.Start(IdempotencyRecordId.New(), organization, operation, key, hash, time.GetUtcNow());
            db.IdempotencyRecords.Add(claim);
            Audit(organization, actor, correlation, alertId, Prefix(kind) + ".requested", expectedVersion);
            try { await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { throw new AssistanceException("operation-in-progress", 409); }
        }
        // The only provider boundary. There is deliberately no open database transaction here.
        string payload;
        string provider;
        string providerVersion;
        string failure = "provider-unavailable";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            if (kind == AssistanceKind.Transcription)
            {
                using var stream = new MemoryStream(audio!, writable: false);
                var output = await transcription.TranscribeAsync(new(stream, contentType!), options, timeout.Token);
                AssistanceValidation.Validate(output);
                if (output.Provider != settings.Capabilities.SpeechProvider) throw new AssistanceException("provider-output-invalid", 503);
                payload = JsonSerializer.Serialize(output, Json); provider = output.Provider; providerVersion = output.ProviderVersion;
            }
            else
            {
                var text = Unprotect(source.Source, organization, ProtectedValuePurposes.AlertTypedSource);
                if (text.Length > AssistanceSettings.MaxTextLength) throw new AssistanceException("source-too-large");
                var output = await structuring.StructureAsync(new(text, options.LanguageHint), timeout.Token);
                AssistanceValidation.Validate(text, output);
                if (output.Provider != "Simulated" || output.ConfigurationVersion != AssistanceSettings.ConfigurationVersion)
                    throw new AssistanceException("provider-output-invalid", 503);
                payload = JsonSerializer.Serialize(output, Json); provider = output.Provider; providerVersion = output.ProviderVersion;
            }
        }
        catch (Exception error)
        {
            if (error is AssistanceException { Code: "provider-output-invalid" }) failure = "provider-output-invalid";
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cleanup.Token);
            var failed = await db.IdempotencyRecords.SingleAsync(row => row.Id == claim.Id, cleanup.Token);
            failed.Complete("failed:" + failure);
            Audit(organization, actor, correlation, alertId, Prefix(kind) + ".failed", expectedVersion);
            await db.SaveChangesAsync(cleanup.Token);
            await transaction.CommitAsync(cleanup.Token);
            throw new AssistanceException(failure, 503);
        }
        db.ChangeTracker.Clear();
        var result = AssistanceResult.Create(Guid.NewGuid(), organization, alertId, new(expectedVersion), source.Id, actor, kind,
            provider, providerVersion, AssistanceSettings.ConfigurationVersion,
            protector.Protect(payload, new(AssistanceResult.PurposeFor(kind), organization.Value)), time.GetUtcNow());
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var claimed = await db.IdempotencyRecords.SingleAsync(row => row.Id == claim.Id, cancellationToken);
            db.AssistanceResults.Add(result);
            claimed.Complete(result.Id.ToString("D"));
            Audit(organization, actor, correlation, alertId, Prefix(kind) + ".completed", expectedVersion);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return await View(result, cancellationToken);
    }

    public async Task<AssistanceApplyResult> ApplyAsync(OrganizationId organization, UserId actor, string correlation, AlertId alertId,
        AssistanceKind kind, Guid resultId, int expectedVersion, string? key, CancellationToken cancellationToken)
    {
        EnsureFeature(kind);
        key = RequireKey(key);
        var operation = Prefix(kind) + ".apply";
        var hash = Hash(organization.Value, actor.Value, alertId.Value, resultId, kind, expectedVersion);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AlertMutationLock.AcquireAsync(db, organization, alertId, cancellationToken);
        var alert = await Load(organization, alertId, cancellationToken);
        var row = await Result(organization, alertId, kind, resultId, cancellationToken);
        var existing = await FindClaim(organization, operation, key, cancellationToken);
        if (existing is not null)
        {
            CheckHash(existing, hash);
            if (!int.TryParse(existing.ResultReference, out var version)) throw new AssistanceException("operation-in-progress", 409);
            return new(alertId.Value, resultId, version, true);
        }
        if (alert.DraftVersion.Value != expectedVersion || row.AlertVersion.Value != expectedVersion
            || alert.CurrentSourceRevision?.Id != row.SourceRevisionId)
        {
            Audit(organization, actor, correlation, alertId, Prefix(kind) + ".stale", expectedVersion);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AssistanceException(kind == AssistanceKind.Transcription ? "transcription-stale" : "suggestion-stale", 409);
        }
        EnsureEditable(alert, expectedVersion);
        var protectedText = Unprotect(row.Payload, organization, AssistanceResult.PurposeFor(kind));
        var now = time.GetUtcNow();
        if (kind == AssistanceKind.Transcription)
        {
            var output = JsonSerializer.Deserialize<TranscriptionResult>(protectedText, Json)!;
            AssistanceValidation.Validate(output);
            // Provider evidence is unchanged. The source is explicitly operator-applied and retains simulation marking.
            var sourceText = SimulationText(output.Transcript);
            alert.UpdateSource(protector.Protect(sourceText, new(ProtectedValuePurposes.AlertTypedSource, organization.Value)),
                new(expectedVersion), now, actor);
            RegisterReview(alert, "transcript", sourceText);
        }
        else
        {
            var sourceText = Unprotect(alert.CurrentSourceRevision!.Source, organization, ProtectedValuePurposes.AlertTypedSource);
            var output = JsonSerializer.Deserialize<AlertStructuringSuggestion>(protectedText, Json)!;
            AssistanceValidation.Validate(sourceText, output);
            var supported = output.Fields.Where(field => AssistanceValidation.IsSupported(sourceText, field)
                && !output.MissingFields.Contains(field.Path) && !output.Ambiguities.Contains(field.Path)).ToArray();
            if (supported.Length == 0) throw new AssistanceException("no-supported-fields", 409);
            // Preserve existing manual values for omitted/unsupported fields; never fabricate replacements.
            var sbar = alert.StructuredSuggestion is null ? new AlertSbarDraft(null, null, null, null)
                : JsonSerializer.Deserialize<AlertSbarDraft>(Unprotect(alert.StructuredSuggestion, organization, ProtectedValuePurposes.AlertSbar), Json)!;
            foreach (var field in supported)
            {
                var value = SimulationText(field.Value);
                sbar = field.Path switch
                {
                    "situation" => sbar with { Situation = value },
                    "background" => sbar with { Background = value },
                    "assessment" => sbar with { Assessment = value },
                    "recommendation" => sbar with { Recommendation = value },
                    _ => sbar,
                };
            }
            alert.SetStructuredSuggestion(protector.Protect(JsonSerializer.Serialize(sbar, Json),
                new(ProtectedValuePurposes.AlertSbar, organization.Value)), new(expectedVersion), now);
            foreach (var field in supported) RegisterReview(alert, field.Path, field.Value);
        }
        var claim = IdempotencyRecord.Start(IdempotencyRecordId.New(), organization, operation, key, hash, now);
        claim.Complete(alert.DraftVersion.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.IdempotencyRecords.Add(claim);
        Audit(organization, actor, correlation, alertId, Prefix(kind) + ".applied", alert.DraftVersion.Value);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(alertId.Value, resultId, alert.DraftVersion.Value, false);
    }

    public async Task<AssistancePage> GetAsync(OrganizationId organization, AlertId alertId, AssistanceKind kind, string? cursor, CancellationToken cancellationToken)
    {
        await Load(organization, alertId, cancellationToken);
        var query = db.AssistanceResults.AsNoTracking().Where(row => row.OrganizationId == organization && row.AlertId == alertId && row.Kind == kind);
        if (cursor is not null)
        {
            AuditCursor position;
            try { position = AuditCursor.Decode(cursor); } catch (Exception) { throw new AssistanceException("cursor-invalid"); }
            query = query.Where(row => row.CreatedAtUtc < position.OccurredAtUtc
                || row.CreatedAtUtc == position.OccurredAtUtc && row.Id.CompareTo(position.Id) < 0);
        }
        var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id).Take(21).ToArrayAsync(cancellationToken);
        var views = new List<AssistanceResultView>();
        foreach (var row in rows.Take(20)) views.Add(await View(row, cancellationToken));
        return new(views, rows.Length > 20 ? AuditCursor.Encode(rows[19].CreatedAtUtc, rows[19].Id) : null);
    }

    private async Task<AssistanceResultView> View(AssistanceResult row, CancellationToken cancellationToken)
    {
        var current = await db.Alerts.AsNoTracking().SingleAsync(alert => alert.Id == row.AlertId && alert.OrganizationId == row.OrganizationId, cancellationToken);
        var revision = await db.AlertSourceRevisions.AsNoTracking().SingleAsync(source => source.Id == row.SourceRevisionId && source.OrganizationId == row.OrganizationId, cancellationToken);
        var payload = Unprotect(row.Payload, row.OrganizationId, AssistanceResult.PurposeFor(row.Kind));
        return new(row.Id, row.AlertId.Value, row.AlertVersion.Value, row.SourceRevisionId.Value, row.CreatedAtUtc, row.Kind.ToString(),
            row.Provider, row.ProviderVersion, row.ConfigurationVersion, row.AlertVersion != current.DraftVersion
                || current.State is not (AlertState.Draft or AlertState.PendingConfirmation),
            Unprotect(revision.Source, row.OrganizationId, ProtectedValuePurposes.AlertTypedSource),
            row.Kind == AssistanceKind.Transcription ? JsonSerializer.Deserialize<TranscriptionResult>(payload, Json) : null,
            row.Kind == AssistanceKind.Structuring ? JsonSerializer.Deserialize<AlertStructuringSuggestion>(payload, Json) : null);
    }
    private async Task<Alert> Load(OrganizationId organization, AlertId id, CancellationToken token) => await db.Alerts
        .Include(a => a.SourceRevisions).Include(a => a.FieldConfirmations).Include(a => a.RecipientSelections).Include(a => a.StateTransitions)
        .SingleOrDefaultAsync(a => a.Id == id && a.OrganizationId == organization, token) ?? throw new AssistanceException("alert-not-found", 404);
    private async Task<AssistanceResult> Result(OrganizationId organization, AlertId alert, AssistanceKind kind, Guid id, CancellationToken token)
        => await db.AssistanceResults.AsNoTracking().SingleOrDefaultAsync(row => row.OrganizationId == organization && row.AlertId == alert && row.Kind == kind && row.Id == id, token)
            ?? throw new AssistanceException("result-not-found", 404);
    private Task<IdempotencyRecord?> FindClaim(OrganizationId org, string operation, string key, CancellationToken token)
        => db.IdempotencyRecords.SingleOrDefaultAsync(row => row.OrganizationId == org && row.OperationType == operation && row.IdempotencyKey == key, token);
    private void EnsureFeature(AssistanceKind kind)
    {
        var on = kind == AssistanceKind.Transcription ? settings.Capabilities.SpeechTranscription : settings.Capabilities.AlertStructuringSuggestions;
        if (!on) throw new AssistanceException(kind == AssistanceKind.Transcription && settings.SpeechRequested ? "provider-unavailable" : "feature-disabled",
            kind == AssistanceKind.Transcription && settings.SpeechRequested ? 503 : 409);
    }
    private static void EnsureEditable(Alert alert, int version)
    {
        if (alert.DraftVersion.Value != version) throw new AssistanceException("draft-version-stale", 409);
        if (alert.State is not (AlertState.Draft or AlertState.PendingConfirmation)) throw new AssistanceException("alert-not-editable", 409);
    }
    private static string RequireKey(string? key) => key is { Length: > 0 and <= 100 } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? key : throw new AssistanceException("idempotency-key-required");
    private static string Hash(params object?[] values) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values))));
    private static void CheckHash(IdempotencyRecord row, string hash)
    { if (!string.Equals(row.RequestHash, hash, StringComparison.Ordinal)) throw new AssistanceException("idempotency-conflict", 409); }
    private static Guid CompletedResult(IdempotencyRecord row)
    {
        if (row.Status != IdempotencyProcessingStatus.Completed) throw new AssistanceException("operation-in-progress", 409);
        if (Guid.TryParse(row.ResultReference, out var id)) return id;
        throw new AssistanceException("provider-unavailable", 503);
    }
    private string Unprotect(ProtectedValue value, OrganizationId organization, string purpose) => protector.Unprotect(value, new(purpose, organization.Value));
    private static string Prefix(AssistanceKind kind) => kind == AssistanceKind.Transcription ? "transcription" : "structuring";
    private static string SimulationText(string value) => value.StartsWith("SIMULATION:", StringComparison.OrdinalIgnoreCase) ? value : "SIMULATION: " + value;
    private static void RegisterReview(Alert alert, string field, string value)
    {
        // This fixed human attestation covers the entire source, including terms outside the
        // deliberately limited simulation lexer. Never copy the transcript into plaintext fields.
        alert.RegisterUnresolvedCriticalField($"assistance-{field}-review",
            "Review all source information, including numbers, units, dates, laterality and medication terms", null, alert.DraftVersion);
        foreach (Match match in Regex.Matches(value,
            @"(?<![\p{L}\d])(?<value>[-+]?\d+(?:[.,:/-]\d+)*)(?:\s*(?<unit>mmHg|mmol/L|mg/dL|mg|mcg|mL|kg|bpm|%|°C)\b)?",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            alert.RegisterUnresolvedCriticalField($"assistance-{field}-{match.Index}", match.Groups["value"].Value,
                match.Groups["unit"].Success ? match.Groups["unit"].Value : null, alert.DraftVersion);
        }
    }
    private void Audit(OrganizationId organization, UserId actor, string correlation, AlertId alert, string action, int version)
        => db.AuditEvents.Add(AuditEvent.Record(AuditEventId.New(), organization, "user", actor, action, "alert", alert.Value,
            action.EndsWith(".failed", StringComparison.Ordinal) ? "failed" : "succeeded", correlation,
            JsonSerializer.Serialize(new { simulationOnly = true, version }), time.GetUtcNow()));
}
