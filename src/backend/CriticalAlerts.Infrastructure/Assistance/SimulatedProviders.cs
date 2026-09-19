using System.Text.RegularExpressions;
using CriticalAlerts.Application.Assistance;

namespace CriticalAlerts.Infrastructure.Assistance;

/// <summary>Returns named fictional scenarios; never recognizes or interprets the supplied audio.</summary>
public sealed class SimulatedTranscriptionProvider : ITranscriptionProvider
{
    public Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scenario = options.SimulationScenario ?? "clear-en";
        var (text, language, confidence) = scenario switch
        {
            "clear-en" => ("Situation: fictional pressure 82/54 mmHg\nBackground: fictional exercise\nAssessment: operator note\nRecommendation: requested callback", "en-CA", (decimal?)0.95m),
            "clear-fr" => ("Situation: pression fictive 82/54 mmHg\nContexte: exercice fictif\nÉvaluation: note opérateur\nRecommandation: rappel demandé", "fr-CA", 0.94m),
            "code-switch" => ("Situation: fictional pression 82/54 mmHg\nContexte: fictional exercise", "en-CA", 0.8m),
            "low-number" => ("Situation: fictional value 18 or 80 mmHg?", "en-CA", 0.41m),
            "missing-unit" => ("Situation: fictional value 82", "en-CA", 0.7m),
            "abbreviation" => ("Assessment: fictional MS?", "en-CA", 0.6m),
            "decimal" => ("Situation: fictional value 8.2 mmol/L", "en-CA", 0.9m),
            "negation" => ("Situation: no fictional pain reported", "en-CA", 0.9m),
            "contradiction" => ("Situation: fictional pain denied\nSituation: fictional pain reported", "en-CA", (decimal?)null),
            "provider-outage" => throw new AssistanceException("provider-unavailable", 503),
            _ => throw new AssistanceException("simulation-scenario-invalid"),
        };
        var transcript = "SIMULATION: " + text;
        return Task.FromResult(new TranscriptionResult(transcript, [new(0, 1000, transcript, confidence)], language, confidence, "Simulated", "DEMO-1"));
    }
}

public sealed class SimulatedAlertStructuringProvider : IAlertStructuringProvider
{
    private static readonly Regex Labels = new(@"(?:\A(?:SIMULATION:\s*)?|\n[ \t]*)(Situation|Background|Contexte|Assessment|Évaluation|Evaluation|Recommendation|Recommandation):[ \t]*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public Task<AlertStructuringSuggestion> StructureAsync(AlertStructuringInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.SourceText.Length > AssistanceSettings.MaxTextLength) throw new AssistanceException("source-too-large");
        var matches = Labels.Matches(input.SourceText);
        var fields = new List<SuggestedField>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var path = match.Groups[1].Value.ToLowerInvariant() switch
            {
                "situation" => "situation",
                "background" or "contexte" => "background",
                "assessment" or "évaluation" or "evaluation" => "assessment",
                _ => "recommendation",
            };
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : input.SourceText.Length;
            while (end > start && char.IsWhiteSpace(input.SourceText[end - 1])) end--;
            if (end <= start) continue;
            var value = input.SourceText[start..end];
            fields.Add(new(path, value, [new(start, end)], null, value.Contains('?')));
        }
        var grouped = fields.GroupBy(field => field.Path).Select(group => group.First() with { Ambiguous = group.Count() > 1 || group.First().Ambiguous }).ToArray();
        var result = new AlertStructuringSuggestion(grouped,
            AssistanceValidation.FieldPaths.Where(path => grouped.All(field => field.Path != path)).ToArray(),
            grouped.Where(field => field.Ambiguous).Select(field => field.Path).ToArray(), null, "Simulated", "DEMO-1", "DEMO-1");
        AssistanceValidation.Validate(input.SourceText, result);
        return Task.FromResult(result);
    }
}
