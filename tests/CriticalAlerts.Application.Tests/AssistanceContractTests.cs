using CriticalAlerts.Application.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class AssistanceContractTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Unknown", false)]
    public void EnvironmentAndExplicitFlagsControlCapabilities(string environment, bool allowed)
    {
        var settings = new AssistanceSettings(environment, true, true, "Simulated", "Simulated");
        settings.Capabilities.SpeechTranscription.Should().Be(allowed);
        settings.Capabilities.AlertStructuringSuggestions.Should().Be(allowed);
        new AssistanceSettings(environment, false, false, "Simulated", "Simulated").Capabilities
            .SpeechTranscription.Should().BeFalse();
    }

    [Fact]
    public void CredentialsCannotEnableSpeechAndInvalidConfigurationFailsClosed()
    {
        new AssistanceSettings("Test", false, false, "AzureSpeech", "Disabled", "canadacentral", "fictional")
            .Capabilities.SpeechTranscription.Should().BeFalse();
        new AssistanceSettings("Test", true, false, "AzureSpeech", "Disabled")
            .Capabilities.SpeechTranscription.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 3, "SIM", true)]
    [InlineData(0, 3, "invented", false)]
    [InlineData(-1, 3, "SIM", false)]
    [InlineData(0, 100, "SIM", false)]
    [InlineData(3, 3, "", false)]
    public void EvidenceRequiresExactExtractAndValidUtf16Bounds(int start, int end, string value, bool supported)
    {
        AssistanceValidation.IsSupported("SIMULATION: fictional", new SuggestedField("situation", value,
            [new EvidenceSpan(start, end)], null, false)).Should().Be(supported);
    }

    [Fact]
    public void EvidenceCannotSplitSurrogatePairsOrApplyAmbiguity()
    {
        AssistanceValidation.IsSupported("😀 test", new SuggestedField("situation", "😀", [new(0, 2)], null, false)).Should().BeTrue();
        AssistanceValidation.IsSupported("😀 test", new SuggestedField("situation", "\uD83D", [new(0, 1)], null, false)).Should().BeFalse();
        AssistanceValidation.IsSupported("SIM", new SuggestedField("situation", "SIM", [new(0, 3)], null, true)).Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void MalformedConfidenceIsRejectedWithoutEchoingContent(double confidence)
    {
        var result = new TranscriptionResult("SIMULATION: SECRET", [], "en-CA", (decimal)confidence, "Simulated", "DEMO-1");
        var action = () => AssistanceValidation.Validate(result);
        action.Should().Throw<AssistanceException>().Which.Message.Should().NotContain("SECRET");
    }

    [Fact]
    public void MissingConfidenceStaysAbsent()
    {
        var result = new TranscriptionResult("SIMULATION: fictional", [], null, null, "Simulated", "DEMO-1");
        AssistanceValidation.Validate(result);
        result.Confidence.Should().BeNull();
    }
}
