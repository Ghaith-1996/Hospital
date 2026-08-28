using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Directory;
using CriticalAlerts.Infrastructure.Identity;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCriticalAlertsPersistence(
        this IServiceCollection services,
        string? connectionString,
        string? dataProtectionKey = null)
    {
        services.AddDbContext<CriticalAlertsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDevelopmentIdentityDirectory, DevelopmentIdentityDirectory>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CsvDirectorySourceAdapter>();
        services.AddScoped<IDirectoryImportService, DirectoryImportService>();
        services.AddScoped<IDirectorySearchService, DirectorySearchService>();
        services.AddScoped<IDirectorySelectionResolver, DirectorySelectionResolver>();
        services.AddScoped<IAlertDraftService, AlertDraftService>();
        services.AddSingleton<ISensitiveDataProtector>(_ => AesGcmSensitiveDataProtector.FromBase64(dataProtectionKey));
        return services;
    }
}
