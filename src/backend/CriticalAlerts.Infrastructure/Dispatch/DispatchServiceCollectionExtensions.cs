using CriticalAlerts.Application.Dispatch;
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
        return services;
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
