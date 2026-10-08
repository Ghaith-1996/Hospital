using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Dispatch;

namespace CriticalAlerts.Api.Authentication;

/// <summary>
/// Provider-neutral voice callback intake settings. Disabled by default; enabled only outside Production and only for
/// the configured non-simulation voice provider, whose <see cref="IVoiceCallbackReader"/> must be registered.
/// </summary>
internal sealed record VoiceWebhookSettings(bool Enabled, string? Provider)
{
    private static readonly HashSet<string> AllowedEnvironments = new(StringComparer.Ordinal) { "Development", "Test", "Staging" };

    public static VoiceWebhookSettings FromConfiguration(IConfiguration configuration, string environmentName)
    {
        if (!configuration.GetValue("Communications:Webhooks:Voice:Enabled", false)) return new VoiceWebhookSettings(false, null);
        if (!AllowedEnvironments.Contains(environmentName))
            throw new InvalidOperationException(
                "The voice callback webhook is limited to Development, Test and Staging. Production enablement is REQUIRES_HOSPITAL_DECISION.");
        var provider = configuration[$"{VoiceDispatchOptions.ConfigurationSection}:Provider"];
        if (!VoiceDispatchOptions.IsValidProviderName(provider) || provider == VoiceDispatchOptions.SimulationProviderName)
            throw new InvalidOperationException("The voice callback webhook requires a configured non-simulation voice provider.");
        return new VoiceWebhookSettings(true, provider);
    }
}

internal static class VoiceWebhookRegistration
{
    public static IServiceCollection AddVoiceWebhook(this IServiceCollection services, VoiceWebhookSettings settings)
    {
        services.AddSingleton(settings);
        if (settings.Enabled)
            services.AddHostedService(provider => new VoiceCallbackReaderStartupCheck(
                provider.GetServices<IVoiceCallbackReader>(), settings.Provider!));
        return services;
    }

    public static IVoiceCallbackReader? Resolve(IEnumerable<IVoiceCallbackReader> readers, string name)
    {
        var matches = readers.Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private sealed class VoiceCallbackReaderStartupCheck(IEnumerable<IVoiceCallbackReader> readers, string name) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => Resolve(readers, name) is null
                ? throw new InvalidOperationException("The configured voice provider's callback reader is not registered exactly once in this host.")
                : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
