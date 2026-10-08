using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CriticalAlerts.Infrastructure.Dispatch;

public static class DispatchServiceCollectionExtensions
{
    public static IServiceCollection AddSimulationDispatch(this IServiceCollection services)
    {
        services.AddScoped<ISimulationDispatchScenarioStore, SimulationDispatchScenarioStore>();
        services.AddScoped<INotificationChannel, SimulationSecureMessageChannel>();
        services.AddScoped<INotificationChannel, SimulationSmsChannel>();
        services.AddScoped<INotificationChannel, SimulationVoiceChannel>();
        services.AddSingleton<INotificationStatusNormalizer, SimulationDeliveryEventNormalizer>();
        services.AddScoped<IOutboxDispatchProcessor, OutboxDispatchProcessor>();
        services.AddScoped<IDeliveryStatusQueryService, DeliveryStatusQueryService>();
        services.AddScoped<IProviderDeliveryReportService, ProviderDeliveryReportService>();
        services.AddScoped<IProviderCallEventService, ProviderCallEventService>();
        return services;
    }

    /// <summary>
    /// Replaces the simulated voice channel with the provider-neutral channel when a provider is explicitly configured.
    /// The named <see cref="IVoiceCallProvider"/> must be registered, which the host verifies at startup with
    /// <see cref="EnsureConfiguredVoiceProviderRegistered"/>; invalid or
    /// production configuration fails startup, and the default remains the simulation.
    /// </summary>
    public static IServiceCollection AddConfiguredVoiceProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        var options = VoiceDispatchOptions.FromConfiguration(configuration, environmentName);
        if (options is null) return services;

        var simulated = services.Where(item => item.ServiceType == typeof(INotificationChannel)
            && item.ImplementationType == typeof(SimulationVoiceChannel)).ToArray();
        foreach (var descriptor in simulated) services.Remove(descriptor);
        services.AddSingleton(options);
        services.AddScoped<INotificationChannel>(provider => new ProviderVoiceChannel(
            options,
            ResolveVoiceProvider(provider.GetServices<IVoiceCallProvider>(), options.ProviderName),
            provider.GetRequiredService<CriticalAlertsDbContext>(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }

    /// <summary>Host startup check: a configured voice provider name must have exactly one registered implementation.</summary>
    public static void EnsureConfiguredVoiceProviderRegistered(IServiceProvider services)
    {
        if (services.GetService<VoiceDispatchOptions>() is { } options)
            _ = ResolveVoiceProvider(services.GetServices<IVoiceCallProvider>(), options.ProviderName);
    }

    internal static IVoiceCallProvider ResolveVoiceProvider(IEnumerable<IVoiceCallProvider> providers, string name)
    {
        var matches = providers.Where(item => string.Equals(item.Name, name, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("The configured voice provider is not registered exactly once in this host.");
    }

    /// <summary>
    /// Replaces the simulated SMS channel with the test-number-only ACS adapter when explicitly configured.
    /// Invalid or production configuration fails startup; the default remains the simulation.
    /// </summary>
    public static IServiceCollection AddConfiguredSmsProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName)
    {
        var options = AcsSmsOptions.FromConfiguration(configuration, environmentName);
        if (options is null) return services;

        var simulated = services.Where(item => item.ServiceType == typeof(INotificationChannel)
            && item.ImplementationType == typeof(SimulationSmsChannel)).ToArray();
        foreach (var descriptor in simulated) services.Remove(descriptor);
        services.AddSingleton(options);
        services.AddSingleton<INotificationChannel>(provider => new AzureCommunicationServicesSmsChannel(
            options,
            AzureCommunicationServicesSmsChannel.CreateDefaultHandler(),
            provider.GetService<TimeProvider>() ?? TimeProvider.System));
        return services;
    }
}
