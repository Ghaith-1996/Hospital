using CriticalAlerts.Application.Assistance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CriticalAlerts.Infrastructure.Assistance;

public static class AssistanceRegistration
{
    public static IServiceCollection AddAssistance(this IServiceCollection services, IConfiguration configuration, string environment)
    {
        var settings = new AssistanceSettings(environment,
            bool.TryParse(configuration["Features:SpeechTranscription"], out var speech) && speech, bool.TryParse(configuration["Features:AlertStructuringSuggestions"], out var structure) && structure,
            configuration["Speech:Provider"] ?? "Disabled", configuration["AlertStructuring:Provider"] ?? "Disabled",
            configuration["Speech:AzureResourceName"], configuration["Speech:AzureKey"]);
        services.AddSingleton(settings);
        services.AddSingleton<ITranscriptionProvider>(settings.Capabilities.SpeechProvider == "Simulated"
            ? new SimulatedTranscriptionProvider() : settings.Capabilities.SpeechProvider == "AzureSpeech"
                ? new AzureSpeechTranscriptionProvider(settings, new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) })
                : new UnavailableTranscriptionProvider());
        services.AddSingleton<IAlertStructuringProvider, SimulatedAlertStructuringProvider>();
        services.AddScoped<IAssistanceService, AssistanceService>();
        return services;
    }
    private sealed class UnavailableTranscriptionProvider : ITranscriptionProvider
    {
        public Task<TranscriptionResult> TranscribeAsync(AudioInput input, TranscriptionOptions options, CancellationToken cancellationToken)
            => throw new AssistanceException("provider-unavailable", 503);
    }
}
