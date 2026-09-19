using System.Text.Json;
using System.Text.RegularExpressions;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Infrastructure.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class AssistanceEvaluationHarnessTests
{
    [Fact]
    public async Task FictionalDatasetProducesOnlySafeAggregateReport()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/backend/CriticalAlerts.sln"))) root = root.Parent;
        root.Should().NotBeNull();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var cases = JsonSerializer.Deserialize<Case[]>(await File.ReadAllTextAsync(Path.Combine(root!.FullName, "tests/fixtures/ai-evaluation/cases.json")), options)!;
        cases.Length.Should().BeGreaterThanOrEqualTo(12);
        var observations = new List<Observation>();
        foreach (var item in cases)
        {
            item.Reference.Should().StartWith("SIMULATION:");
            string transcript;
            if (item.Scenario is null) transcript = item.Reference;
            else
            {
                using var audio = new MemoryStream([1, 2, 3]);
                try { transcript = (await new SimulatedTranscriptionProvider().TranscribeAsync(new(audio, "audio/wav"), new(null, item.Scenario), default)).Transcript; }
                catch (AssistanceException) when (item.Failure) { observations.Add(new(item.Group, true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)); continue; }
            }
            item.Failure.Should().BeFalse("the outage fixture must fail");
            var suggestion = await new SimulatedAlertStructuringProvider().StructureAsync(new(transcript, null), default);
            AssistanceValidation.Validate(transcript, suggestion);
            suggestion.MissingFields.Order().Should().Equal(item.Missing.Order());
            suggestion.Ambiguities.Order().Should().Equal(item.Ambiguous.Order());
            // Score provider output against independently authored fixtures, not against the same extractor.
            var fields = suggestion.Fields.Where(field => !field.Ambiguous).ToArray();
            var values = string.Join("\n", fields.Select(field => field.Value));
            var numberTokens = Regex.Matches(transcript, @"(?<![\p{L}\d])[-+]?\d+(?:[.,:/-]\d+)*", RegexOptions.CultureInvariant).Select(match => match.Value).ToArray();
            var actualPairs = Regex.Matches(transcript, @"(?<n>[-+]?\d+(?:[.,:/-]\d+)*)\s+(?<u>mmHg|mmol/L|mg|bpm)\b", RegexOptions.CultureInvariant)
                .Select(match => match.Groups["n"].Value + "|" + match.Groups["u"].Value).ToArray();
            var unsupported = suggestion.Fields.Count(field => !field.Evidence.Any() ||
                field.Value != string.Join("\n", field.Evidence.Select(span => transcript[span.Start..span.EndExclusive])));
            var omitted = item.ExpectedFields.Count(path => fields.All(field => field.Path != path));
            var errors = EvaluationMetrics.WordErrors(item.Reference, transcript);
            var corrected = suggestion.Fields.Count(field => !item.ApprovedFields.TryGetValue(field.Path, out var approved) || approved != field.Value);
            observations.Add(new(item.Group, false, errors, EvaluationMetrics.Words(item.Reference).Length,
                EvaluationMetrics.ExactMatches(item.Numbers, numberTokens), item.Numbers.Length,
                EvaluationMetrics.ExactMatches(item.Pairs, actualPairs), item.Pairs.Length, unsupported, suggestion.Fields.Count,
                omitted, item.ExpectedFields.Length, corrected, suggestion.MissingFields.Count, suggestion.Ambiguities.Count, item.Scenario is not null));
        }
        observations.Count(row => row.Failure).Should().Be(1);
        observations.Sum(row => row.Unsupported).Should().Be(0);
        observations.Sum(row => row.Omitted).Should().Be(0);
        var groups = observations.GroupBy(row => row.Group).Select(group => Aggregate(group.Key, group.ToArray())).ToArray();
        var report = new
        {
            simulationOnly = true,
            provider = "Simulated",
            configurationVersion = "DEMO-1",
            speechRecognitionMeasured = false,
            correctionMeasure = "fictional-operator-approved-fields",
            totals = Aggregate("all", observations.ToArray()),
            languages = groups
        };
        var json = JsonSerializer.Serialize(report, options);
        json.Should().NotContain("SIMULATION:").And.NotContain("fictional pressure").And.NotContain("DisplayText");
        if (Environment.GetEnvironmentVariable("ASSISTANCE_EVALUATION_REPORT") == "true")
        {
            var directory = Path.Combine(root.FullName, "artifacts", "ai-evaluation"); System.IO.Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), json);
        }
    }
    private static object Aggregate(string group, Observation[] rows)
    {
        var succeeded = rows.Where(row => !row.Failure).ToArray();
        var speech = succeeded.Where(row => row.Speech).ToArray();
        return new
        {
            group,
            cases = rows.Length,
            providerFailures = rows.Count(row => row.Failure),
            wordErrors = speech.Sum(row => row.WordErrors),
            referenceWords = speech.Sum(row => row.Words),
            wordErrorRate = EvaluationMetrics.Rate(speech.Sum(row => row.WordErrors), speech.Sum(row => row.Words)),
            criticalNumberExactMatches = succeeded.Sum(row => row.NumberMatches),
            criticalNumberCount = succeeded.Sum(row => row.Numbers),
            criticalNumberExactMatchRate = EvaluationMetrics.Rate(succeeded.Sum(row => row.NumberMatches), succeeded.Sum(row => row.Numbers)),
            unitExactMatches = succeeded.Sum(row => row.PairMatches),
            unitPairCount = succeeded.Sum(row => row.Pairs),
            unitPairExactMatchRate = EvaluationMetrics.Rate(succeeded.Sum(row => row.PairMatches), succeeded.Sum(row => row.Pairs)),
            unsupportedInferenceCount = succeeded.Sum(row => row.Unsupported),
            suggestedFieldCount = succeeded.Sum(row => row.Fields),
            unsupportedInferenceRate = EvaluationMetrics.Rate(succeeded.Sum(row => row.Unsupported), succeeded.Sum(row => row.Fields)),
            omissionCount = succeeded.Sum(row => row.Omitted),
            expectedFieldCount = succeeded.Sum(row => row.ExpectedFields),
            omissionRate = EvaluationMetrics.Rate(succeeded.Sum(row => row.Omitted), succeeded.Sum(row => row.ExpectedFields)),
            humanCorrectionFieldCount = succeeded.Sum(row => row.Corrected),
            humanCorrectionRate = EvaluationMetrics.Rate(succeeded.Sum(row => row.Corrected), succeeded.Sum(row => row.Fields)),
            missingFieldCount = succeeded.Sum(row => row.Missing),
            ambiguityCount = succeeded.Sum(row => row.Ambiguous)
        };
    }
    private sealed record Case(string Id, string Group, string? Scenario, string Reference, string[] Numbers, string[] Pairs, string[] ExpectedFields, string[] Missing, string[] Ambiguous, Dictionary<string, string> ApprovedFields, bool Failure = false);
    private sealed record Observation(string Group, bool Failure, int WordErrors, int Words, int NumberMatches, int Numbers, int PairMatches, int Pairs,
        int Unsupported, int Fields, int Omitted, int ExpectedFields, int Corrected, int Missing, int Ambiguous, bool Speech = false);
}
