using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Identity;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Infrastructure.Alerts;
using CriticalAlerts.Infrastructure.Directory;
using CriticalAlerts.Infrastructure.Identity;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using CriticalAlerts.Infrastructure.Protection;
using CriticalAlerts.Infrastructure.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CriticalAlerts.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCriticalAlertsPersistence(
        this IServiceCollection services,
        string? connectionString,
        string? dataProtectionKey = null)
    {
        services.AddSingleton<PlatformMetrics>();
        services.AddScoped(provider => new CommittedOperations(provider.GetRequiredService<ILoggerFactory>().CreateLogger(CriticalAlertsOperationalLog.Category), provider.GetRequiredService<PlatformMetrics>()));
        services.AddScoped<OperationSaveInterceptor>();
        services.AddScoped<OperationTransactionInterceptor>();
        services.AddDbContext<CriticalAlertsDbContext>((provider, options) => options.UseNpgsql(connectionString)
            .AddInterceptors(provider.GetRequiredService<OperationSaveInterceptor>(), provider.GetRequiredService<OperationTransactionInterceptor>()));
        services.AddScoped<IDevelopmentIdentityDirectory, DevelopmentIdentityDirectory>();
        services.AddScoped<CriticalAlerts.Application.Audit.IAuditQueryService, CriticalAlerts.Infrastructure.Audit.AuditQueryService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CsvDirectorySourceAdapter>();
        services.AddScoped<IDirectoryImportService, DirectoryImportService>();
        services.AddScoped<IDirectorySearchService, DirectorySearchService>();
        services.AddScoped<IDirectorySelectionResolver, DirectorySelectionResolver>();
        services.AddScoped<IAlertDraftService, AlertDraftService>();
        services.AddScoped<IAlertReviewService, AlertReviewService>();
        services.AddScoped<PractitionerIdentityResolver>();
        services.AddScoped<IRecipientInboxService, RecipientInboxService>();
        services.AddScoped<IRecipientResponseService, RecipientResponseService>();
        services.AddScoped<IAlertLiveQueryService, AlertLiveQueryService>();
        services.AddScoped<IAlertLifecycleService, AlertLifecycleService>();
        services.AddSingleton<ISensitiveDataProtector>(_ => AesGcmSensitiveDataProtector.FromBase64(dataProtectionKey));
        return services;
    }
}
