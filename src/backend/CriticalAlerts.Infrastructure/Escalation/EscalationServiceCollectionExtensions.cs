using Microsoft.Extensions.DependencyInjection;

namespace CriticalAlerts.Infrastructure.Escalation;

public static class EscalationServiceCollectionExtensions
{
    public static IServiceCollection AddSimulationEscalation(this IServiceCollection services)
    {
        services.AddScoped<DatabaseClock>();
        services.AddScoped<EscalationScheduler>();
        services.AddScoped<EscalationRunRepository>();
        services.AddScoped<EscalationRunProcessor>();
        return services;
    }
}
