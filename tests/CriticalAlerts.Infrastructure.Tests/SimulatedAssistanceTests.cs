using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Infrastructure.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class SimulatedAssistanceTests
{
    [Fact]
    public async Task ExtractsExactEvidenceWithoutInventingMissingFields()
    {
        const string source = "SIMULATION: Situation: fictional BP 82/54 mmHg\nBackground: no prior event";
        var result = await new SimulatedAlertStructuringProvider().StructureAsync(new(source, "en-CA"), default);
        result.Fields.Single(f => f.Path == "situation").Value.Should().Be("fictional BP 82/54 mmHg");
        result.MissingFields.Should().BeEquivalentTo("assessment", "recommendation");
        result.Fields.Should().OnlyContain(f => AssistanceValidation.IsSupported(source, f));
    }

    [Fact]
    public async Task DuplicateLabelsRemainAmbiguousAndNegationIsNotDropped()
    {
        const string source = "SIMULATION: Situation: no pain\nSituation: pain reported\nContexte: aucune douleur";
        var result = await new SimulatedAlertStructuringProvider().StructureAsync(new(source, "fr-CA"), default);
        result.Fields.Single(f => f.Path == "situation").Ambiguous.Should().BeTrue();
        result.Fields.Single(f => f.Path == "background").Value.Should().Be("aucune douleur");
        result.Ambiguities.Should().Contain("situation");
    }

    [Theory]
    [InlineData("clear-en", "en-CA")]
    [InlineData("clear-fr", "fr-CA")]
    [InlineData("code-switch", "en-CA")]
    [InlineData("low-number", "en-CA")]
    [InlineData("missing-unit", "en-CA")]
    [InlineData("abbreviation", "en-CA")]
    [InlineData("decimal", "en-CA")]
    [InlineData("negation", "en-CA")]
    [InlineData("contradiction", "en-CA")]
    public async Task SimulationIsDeterministicAndDoesNotInterpretAudio(string scenario, string language)
    {
        var provider = new SimulatedTranscriptionProvider();
        using var bytes = new MemoryStream([1, 2, 3]);
        var result = await provider.TranscribeAsync(new(bytes, "audio/webm;codecs=opus"), new(null, scenario), default);
        var again = await provider.TranscribeAsync(new(bytes, "audio/webm;codecs=opus"), new(null, scenario), default);
        result.Should().BeEquivalentTo(again);
        result.DetectedLanguage.Should().Be(language);
        result.Transcript.Should().StartWith("SIMULATION:");
        AssistanceValidation.Validate(result);
    }
}
