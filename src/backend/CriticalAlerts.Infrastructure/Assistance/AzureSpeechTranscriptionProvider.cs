using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CriticalAlerts.Application.Assistance;

namespace CriticalAlerts.Infrastructure.Assistance;

/// <summary>Opt-in fictional short-audio adapter. No retries, payload logging, files or provider diagnostics.</summary>
public sealed class AzureSpeechTranscriptionProvider(AssistanceSettings settings, HttpClient client) : ITranscriptionProvider, IDisposable
{
    public async Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken cancellationToken)
    {
        if (!settings.Capabilities.SpeechTranscription || settings.Capabilities.SpeechProvider != "AzureSpeech")
            throw new AssistanceException("provider-unavailable", 503);
        if (input.ContentType != AssistanceSettings.WaveContentType) throw new AssistanceException("audio-type-unsupported", 415);
        if (!AssistanceValidation.LanguageAllowed(options.LanguageHint) || options.SimulationScenario is not null) throw new AssistanceException("language-invalid");
        byte[]? audio = null;
        byte[]? responseBytes = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            audio = await ReadBounded(input.Content, AssistanceSettings.MaxAudioBytes, timeout.Token);
            ValidateWave(audio);
            var language = options.LanguageHint ?? "en-CA";
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://{settings.AzureResourceName}.cognitiveservices.azure.com/stt/speech/recognition/conversation/cognitiveservices/v1?language={language}&format=simple&profanity=raw");
            request.Headers.Add("Ocp-Apim-Subscription-Key", settings.AzureKey);
            request.Headers.Accept.Add(new("application/json"));
            request.Content = new ByteArrayContent(audio);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", "audio/wav; codecs=audio/pcm; samplerate=16000");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new AssistanceException("provider-unavailable", 503);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            responseBytes = await ReadBounded(body, 128 * 1024, timeout.Token);
            using var json = JsonDocument.Parse(responseBytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            if (root.GetProperty("RecognitionStatus").GetString() != "Success") throw new AssistanceException("provider-unavailable", 503);
            var transcript = root.GetProperty("DisplayText").GetString()!;
            var segments = new List<TranscriptionSegment>();
            if (root.TryGetProperty("Duration", out var duration))
            {
                var ticks = duration.ValueKind == JsonValueKind.String ? long.Parse(duration.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : duration.GetInt64();
                var offset = root.TryGetProperty("Offset", out var start) ? start.ValueKind == JsonValueKind.String
                    ? long.Parse(start.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : start.GetInt64() : 0;
                if (ticks <= 0 || ticks > 600000000 || offset < 0 || offset + ticks > 600000000) throw new AssistanceException("provider-output-invalid", 503);
                segments.Add(new((int)(offset / 10000), (int)((offset + ticks + 9999) / 10000), transcript, null));
            }
            // Simple mode supplies neither calibrated confidence nor detected language. A hint is not detection.
            var result = new TranscriptionResult(transcript, segments, null, null, "AzureSpeech", "short-audio-v1");
            AssistanceValidation.Validate(result);
            return result;
        }
        catch (AssistanceException) { throw; }
        catch (Exception) { throw new AssistanceException("provider-unavailable", 503); }
        finally
        {
            if (audio is not null) CryptographicOperations.ZeroMemory(audio);
            if (responseBytes is not null) CryptographicOperations.ZeroMemory(responseBytes);
        }
    }
    private static async Task<byte[]> ReadBounded(Stream stream, int maximum, CancellationToken token)
    {
        var buffer = new byte[maximum + 1];
        try
        {
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), token);
                if (read == 0) break;
                length += read;
            }
            if (length == 0 || length > maximum) throw new AssistanceException("provider-input-or-output-invalid", 503);
            return buffer.AsSpan(0, length).ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    private static void ValidateWave(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 44 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) != bytes.Length - 8) throw new AssistanceException("audio-invalid");
        var format = false;
        var data = false;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            if (size > bytes.Length - position - 8) throw new AssistanceException("audio-invalid");
            var chunk = bytes.Slice(position + 8, (int)size);
            if (bytes.Slice(position, 4).SequenceEqual("fmt "u8))
            {
                if (format || size != 16 || BinaryPrimitives.ReadUInt16LittleEndian(chunk) != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]) != 16000
                    || BinaryPrimitives.ReadUInt32LittleEndian(chunk[8..]) != 32000 || BinaryPrimitives.ReadUInt16LittleEndian(chunk[12..]) != 2
                    || BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]) != 16) throw new AssistanceException("audio-invalid");
                format = true;
            }
            if (bytes.Slice(position, 4).SequenceEqual("data"u8))
            {
                if (!format || data || size == 0 || size > 60 * 32000 || size % 2 != 0) throw new AssistanceException("audio-invalid");
                data = true;
            }
            position += 8 + (int)size + (int)(size % 2);
        }
        if (!format || !data || position != bytes.Length) throw new AssistanceException("audio-invalid");
    }
    public void Dispose() => client.Dispose();
}
