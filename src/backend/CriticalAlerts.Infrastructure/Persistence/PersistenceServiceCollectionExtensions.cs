using CriticalAlerts.Application.Identity;
using CriticalAlerts.Infrastructure.Identity;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCriticalAlertsPersistence(this IServiceCollection services, string? connectionString)
    {
        services.AddDbContext<CriticalAlertsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDevelopmentIdentityDirectory, DevelopmentIdentityDirectory>();
        return services;
    }
}
