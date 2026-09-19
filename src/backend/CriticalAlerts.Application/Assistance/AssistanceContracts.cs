using System.Text.RegularExpressions;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Assistance;

public sealed record AudioInput(Stream Content, string ContentType);
public sealed record TranscriptionOptions(string? LanguageHint, string? SimulationScenario = null);
public sealed record TranscriptionSegment(int StartMilliseconds, int EndMilliseconds, string Text, decimal? Confidence);
public sealed record TranscriptionResult(string Transcript, IReadOnlyList<TranscriptionSegment> Segments,
    string? DetectedLanguage, decimal? Confidence, string Provider, string ProviderVersion);
public sealed record AlertStructuringInput(string SourceText, string? Language);
public sealed record EvidenceSpan(int Start, int EndExclusive);
public sealed record SuggestedField(string Path, string Value, IReadOnlyList<EvidenceSpan> Evidence, decimal? Confidence, bool Ambiguous);
public sealed record AlertStructuringSuggestion(IReadOnlyList<SuggestedField> Fields, IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> Ambiguities, decimal? Confidence, string Provider, string ProviderVersion, string ConfigurationVersion);
public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken cancellationToken);
}
public interface IAlertStructuringProvider
{
    Task<AlertStructuringSuggestion> StructureAsync(AlertStructuringInput input, CancellationToken cancellationToken);
}
public sealed record AssistanceCapabilities(bool SpeechTranscription, bool AlertStructuringSuggestions, string SpeechProvider,
    IReadOnlyList<string> AcceptedAudioContentTypes, bool SimulationOnly = true);

public sealed class AssistanceException(string code, int status = 400) : Exception("Assistance could not be completed. Continue with manual editing.")
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class AssistanceSettings
{
    public const int MaxTextLength = 16000;
    public const int MaxAudioBytes = 2 * 1024 * 1024;
    public const string ConfigurationVersion = "DEMO-1";
    public const string WaveContentType = "audio/wav";
    public string? AzureRegion { get; }
    public string? AzureKey { get; }
    public bool EnvironmentAllowed { get; }
    public bool SpeechRequested { get; }
    public bool StructuringRequested { get; }
    public AssistanceCapabilities Capabilities { get; }
    public AssistanceSettings(string environment, bool speech, bool structuring, string speechProvider,
        string structuringProvider, string? azureRegion = null, string? azureKey = null)
    {
        EnvironmentAllowed = environment is "Development" or "Test";
        SpeechRequested = speech && EnvironmentAllowed;
        StructuringRequested = structuring && EnvironmentAllowed;
        AzureRegion = azureRegion;
        AzureKey = azureKey;
        var configured = !string.IsNullOrWhiteSpace(azureKey) && azureKey.Length <= 256
            && azureKey.All(char.IsAsciiLetterOrDigit) && azureRegion is not null
            && Regex.IsMatch(azureRegion, "^[a-z]{2,30}[0-9]?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var speechOn = SpeechRequested && (speechProvider == "Simulated" || speechProvider == "AzureSpeech" && configured);
        Capabilities = new(speechOn, StructuringRequested && structuringProvider == "Simulated",
            speechOn ? speechProvider : "Disabled",
            speechOn ? speechProvider == "AzureSpeech" ? [WaveContentType] :
                ["audio/webm;codecs=opus", "audio/ogg;codecs=opus", "audio/mp4", WaveContentType] : []);
    }
}

public static class AssistanceValidation
{
    public static readonly IReadOnlyList<string> FieldPaths = Array.AsReadOnly(new[] { "situation", "background", "assessment", "recommendation" });
    public static bool IsSupported(string source, SuggestedField field)
    {
        if (field.Ambiguous || string.IsNullOrWhiteSpace(field.Value) || field.Evidence is not { Count: > 0 and <= 8 }) return false;
        if (field.Evidence.Any(span => !ValidSpan(source, span))) return false;
        return string.Equals(field.Value, string.Join("\n", field.Evidence.Select(span => source[span.Start..span.EndExclusive])), StringComparison.Ordinal);
    }
    public static bool ValidSpan(string source, EvidenceSpan span) => span.Start >= 0 && span.EndExclusive > span.Start
        && span.EndExclusive <= source.Length && !(span.Start > 0 && char.IsLowSurrogate(source[span.Start]))
        && !(span.EndExclusive < source.Length && char.IsLowSurrogate(source[span.EndExclusive]));
    public static bool LanguageAllowed(string? language) => language is null or "en-CA" or "en-US" or "fr-CA" or "fr-FR";
    public static void Validate(TranscriptionResult result)
    {
        if (result is null || !Text(result.Transcript, AssistanceSettings.MaxTextLength) || !Confidence(result.Confidence)
            || !LanguageAllowed(result.DetectedLanguage) || !Provider(result.Provider) || !Version(result.ProviderVersion)
            || result.Segments is null || result.Segments.Count > 100
            || result.Segments.Any(segment => segment is null || segment.StartMilliseconds < 0
                || segment.EndMilliseconds <= segment.StartMilliseconds || segment.EndMilliseconds > 60000
                || !Text(segment.Text, AssistanceSettings.MaxTextLength) || !Confidence(segment.Confidence))) Invalid();
    }
    public static void Validate(string source, AlertStructuringSuggestion result)
    {
        if (result is null || result.Fields is null || result.MissingFields is null || result.Ambiguities is null
            || result.Fields.Count > 4 || result.MissingFields.Count > 4 || result.Ambiguities.Count > 4
            || !Confidence(result.Confidence) || !Provider(result.Provider) || !Version(result.ProviderVersion)
            || !Version(result.ConfigurationVersion)) Invalid();
        if (result!.Fields.Any(field => field is null || !FieldPaths.Contains(field.Path) || !Text(field.Value, 4000)
            || !Confidence(field.Confidence) || field.Evidence is null || field.Evidence.Count > 8
            || field.Evidence.Any(span => span is null || !ValidSpan(source, span)))
            || result.Fields.Select(field => field.Path).Distinct().Count() != result.Fields.Count
            || result.MissingFields.Concat(result.Ambiguities).Any(path => !FieldPaths.Contains(path))) Invalid();
    }
    private static bool Text(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) && text.Length <= maximum && !text.Contains('\0');
    private static bool Confidence(decimal? confidence) => confidence is null or >= 0 and <= 1;
    private static bool Provider(string? provider) => provider is "Simulated" or "AzureSpeech";
    private static bool Version(string? version) => version is { Length: > 0 and <= 40 }
        && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_');
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new AssistanceException("provider-output-invalid", 503);
}
