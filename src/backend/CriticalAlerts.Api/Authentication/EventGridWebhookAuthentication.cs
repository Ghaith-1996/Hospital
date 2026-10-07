using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace CriticalAlerts.Api.Authentication;

/// <summary>
/// Settings for authenticated Azure Event Grid delivery-report webhooks. Disabled by default, never in
/// Production, and fail closed when partially configured. Values are never echoed in errors.
/// </summary>
internal sealed record EventGridWebhookSettings(
    bool Enabled,
    string TenantId,
    string Audience,
    string ExpectedTopic,
    string SubscriptionName,
    string SenderApplicationId)
{
    public const string Section = "Communications:Webhooks:EventGrid";
    public const string Scheme = "EventGridWebhook";
    public const string Policy = "EventGridWebhookSender";
    public const string RequiredRole = "AzureEventGridSecureWebhookSubscriber";

    public static EventGridWebhookSettings FromConfiguration(IConfiguration configuration, string environmentName)
    {
        var section = configuration.GetSection(Section);
        if (!section.GetValue("Enabled", false)) return new(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        if (environmentName is not ("Development" or "Test" or "Staging"))
            throw new InvalidOperationException(
                "Provider delivery-report webhooks are limited to Development, Test and Staging. Production is REQUIRES_HOSPITAL_DECISION.");

        var tenant = section["TenantId"];
        var audience = section["Audience"];
        var topic = section["ExpectedTopic"];
        // Name of the intended Event Grid event subscription. Event Grid sends it as aeg-subscription-name on every
        // delivery, including the handshake, whose body topic does not identify the subscription.
        var subscriptionName = section["SubscriptionName"];
        if (!IsValidSubscriptionName(subscriptionName))
        {
            throw new InvalidOperationException(
                "Communications:Webhooks:EventGrid requires the SubscriptionName of the intended Event Grid subscription.");
        }

        // The Microsoft.EventGrid application ID in this tenant's cloud (read from its service principal). The
        // subscription writer also holds the webhook app role, so the role alone does not prove Event Grid sent it.
        var sender = section["SenderApplicationId"];
        if (!Guid.TryParseExact(sender, "D", out _))
        {
            throw new InvalidOperationException(
                "Communications:Webhooks:EventGrid requires the SenderApplicationId of the Microsoft.EventGrid service principal.");
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

        return new(true, tenant!, audience!, topic!, subscriptionName!, sender!);
    }

    /// <summary>Event Grid subscription names: 3-64 letters, digits and hyphens.</summary>
    public static bool IsValidSubscriptionName(string? value)
        => value is { Length: >= 3 and <= 64 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    public bool IsIntendedSubscription(string? headerValue)
        => IsValidSubscriptionName(headerValue) && string.Equals(headerValue, SubscriptionName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The token's client is the configured Event Grid principal: v1 tokens carry it as <c>appid</c>, v2 as <c>azp</c>.
    /// Every sender claim present must name it; a missing sender, or a foreign or conflicting one, is refused.
    /// </summary>
    public bool IsEventGridSender(ClaimsPrincipal principal)
    {
        var senders = principal.FindAll("appid").Concat(principal.FindAll("azp")).Select(claim => claim.Value).ToArray();
        return senders.Length > 0
            && senders.All(value => string.Equals(value, SenderApplicationId, StringComparison.OrdinalIgnoreCase));
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
            .RequireRole(EventGridWebhookSettings.RequiredRole)
            .RequireAssertion(context => settings.IsEventGridSender(context.User)));
        return services;
    }
}
