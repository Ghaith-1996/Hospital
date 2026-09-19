using System.Net;
using System.Text;
using CriticalAlerts.Application.Assistance;
using CriticalAlerts.Infrastructure.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class AzureSpeechAdapterTests
{
    [Fact]
    public async Task MapsTranscriptWithoutInventingConfidenceAndUsesOnlyVerifiedWaveTransport()
    {
        var transport = new Transport(HttpStatusCode.OK, "{\"RecognitionStatus\":\"Success\",\"DisplayText\":\"fictional transcript\",\"Duration\":10000000}");
        using var http = new HttpClient(transport);
        using var provider = new AzureSpeechTranscriptionProvider(Settings(), http);
        using var audio = new MemoryStream(Wave());
        var result = await provider.TranscribeAsync(new(audio, "audio/wav"), new("fr-CA"), default);
        result.Transcript.Should().Be("fictional transcript");
        result.Confidence.Should().BeNull();
        result.DetectedLanguage.Should().BeNull("a configured hint is not detected language");
        transport.RequestUri!.Host.Should().Be("simulation-speech.cognitiveservices.azure.com");
        transport.RequestUri.Query.Should().Contain("language=fr-CA");
        transport.ContentType.Should().Be("audio/wav; codecs=audio/pcm; samplerate=16000");
    }
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task NormalizesProviderFailuresWithoutEchoingBodies(HttpStatusCode status)
    {
        using var provider = new AzureSpeechTranscriptionProvider(Settings(), new HttpClient(new Transport(status, "SIM-PROVIDER-SECRET")));
        using var audio = new MemoryStream(Wave());
        var failure = await Record.ExceptionAsync(() => provider.TranscribeAsync(new(audio, "audio/wav"), new(null), default));
        failure.Should().BeOfType<AssistanceException>().Which.ToString().Should().NotContain("SIM-PROVIDER-SECRET");
    }
    [Fact]
    public async Task DisabledAndMalformedAudioMakeNoNetworkRequest()
    {
        var transport = new Transport(HttpStatusCode.OK, "{}");
        using var disabled = new AzureSpeechTranscriptionProvider(new("Test", false, false, "AzureSpeech", "Disabled"), new HttpClient(transport));
        using var audio = new MemoryStream(Wave());
        await Assert.ThrowsAsync<AssistanceException>(() => disabled.TranscribeAsync(new(audio, "audio/wav"), new(null), default));
        using var enabled = new AzureSpeechTranscriptionProvider(Settings(), new HttpClient(transport));
        using var malformed = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<AssistanceException>(() => enabled.TranscribeAsync(new(malformed, "audio/wav"), new(null), default));
        await Assert.ThrowsAsync<AssistanceException>(() => enabled.TranscribeAsync(new(audio, "audio/webm"), new(null), default));
        transport.Calls.Should().Be(0);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"RecognitionStatus\":\"NoMatch\"}")]
    [InlineData("{\"RecognitionStatus\":\"Success\",\"DisplayText\":\"\"}")]
    [InlineData("{\"RecognitionStatus\":\"Success\",\"DisplayText\":\"SIMULATION: bounded\",\"Duration\":600000001}")]
    public async Task MalformedResponseNeverEscapesAsUsableTranscript(string body)
    {
        using var provider = new AzureSpeechTranscriptionProvider(Settings(), new HttpClient(new Transport(HttpStatusCode.OK, body)));
        using var audio = new MemoryStream(Wave());
        await Assert.ThrowsAsync<AssistanceException>(() => provider.TranscribeAsync(new(audio, "audio/wav"), new(null), default));
    }
    [Fact]
    public async Task OversizedResponseAndCancellationAreSafeAndNeverRetried()
    {
        var transport = new Transport(HttpStatusCode.OK, new string('x', 128 * 1024 + 1));
        using var provider = new AzureSpeechTranscriptionProvider(Settings(), new HttpClient(transport));
        using var audio = new MemoryStream(Wave());
        await Assert.ThrowsAsync<AssistanceException>(() => provider.TranscribeAsync(new(audio, "audio/wav"), new(null), default));
        transport.Calls.Should().Be(1);
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        using var secondAudio = new MemoryStream(Wave());
        await Assert.ThrowsAsync<AssistanceException>(() => provider.TranscribeAsync(new(secondAudio, "audio/wav"), new(null), stopped.Token));
        transport.Calls.Should().Be(1);
    }
    internal static byte[] Wave()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(38); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(2); writer.Write((short)0);
        return stream.ToArray();
    }
    private static AssistanceSettings Settings() => new("Test", true, false, "AzureSpeech", "Disabled", "simulation-speech", new string('x', 32));
    private sealed class Transport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ContentType { get; private set; }
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; RequestUri = request.RequestUri; ContentType = request.Content!.Headers.GetValues("Content-Type").Single();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
