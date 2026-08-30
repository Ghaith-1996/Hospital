using CriticalAlerts.Application.Dispatch;
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
}
