using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CriticalAlerts.Api.Authentication;

/// <summary>
/// Settings for authenticated Azure Event Grid delivery-report webhooks. Disabled by default, never in
/// Production, and fail closed when partially configured. Values are never echoed in errors.
/// </summary>
internal sealed record EventGridWebhookSettings(bool Enabled, string TenantId, string Audience, string ExpectedTopic, string ValidationTopic)
{
    public const string Section = "Communications:Webhooks:EventGrid";
    public const string Scheme = "EventGridWebhook";
    public const string Policy = "EventGridWebhookSender";
    public const string RequiredRole = "AzureEventGridSecureWebhookSubscriber";

    public static EventGridWebhookSettings FromConfiguration(IConfiguration configuration, string environmentName)
    {
        var section = configuration.GetSection(Section);
        if (!section.GetValue("Enabled", false)) return new(false, string.Empty, string.Empty, string.Empty, string.Empty);
        if (environmentName is not ("Development" or "Test" or "Staging"))
            throw new InvalidOperationException(
                "Provider delivery-report webhooks are limited to Development, Test and Staging. Production is REQUIRES_HOSPITAL_DECISION.");

        var tenant = section["TenantId"];
        var audience = section["Audience"];
        var topic = section["ExpectedTopic"];
        // The Event Grid topic the subscription is created on; only its validation handshake is answered.
        var validationTopic = section["ValidationTopic"];
        if (string.IsNullOrWhiteSpace(validationTopic) || validationTopic.Length > 400
            || !validationTopic.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Communications:Webhooks:EventGrid requires the ValidationTopic resource ID of the intended Event Grid subscription.");
        }

        if (!Guid.TryParseExact(tenant, "D", out _)
            || string.IsNullOrWhiteSpace(audience) || audience.Length > 200
            || string.IsNullOrWhiteSpace(topic) || topic.Length > 400
            || !topic.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase)
            || !topic.Contains("/providers/microsoft.communication/communicationservices/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Communications:Webhooks:EventGrid requires TenantId, Audience and an ACS ExpectedTopic resource ID.");
        }

        return new(true, tenant!, audience!, topic!, validationTopic);
    }
}

internal static class EventGridWebhookAuthentication
{
    public static IServiceCollection AddEventGridWebhookAuthentication(this IServiceCollection services, EventGridWebhookSettings settings)
    {
        services.AddSingleton(settings);
        if (!settings.Enabled) return services;

        services.AddAuthentication().AddJwtBearer(EventGridWebhookSettings.Scheme, options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{settings.TenantId}/v2.0";
            options.RequireHttpsMetadata = true;
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.SaveToken = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuers =
                [
                    $"https://login.microsoftonline.com/{settings.TenantId}/v2.0",
                    $"https://sts.windows.net/{settings.TenantId}/",
                ],
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromMinutes(2),
                RoleClaimType = "roles",
                NameClaimType = "appid",
            };
        });
        services.AddAuthorizationBuilder().AddPolicy(EventGridWebhookSettings.Policy, policy => policy
            .AddAuthenticationSchemes(EventGridWebhookSettings.Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(EventGridWebhookSettings.RequiredRole));
        return services;
    }
}
