using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CriticalAlerts.Api.Authentication;

/// <summary>
/// Reusable OIDC-style callback authentication for provider webhooks that sign each request with a short-lived RS256
/// bearer token (for example ACS Call Automation callbacks). Validates signature against the provider's published keys,
/// issuer, audience and lifetime, and refuses long-lived tokens because such tokens are not bound to the request body.
/// </summary>
public sealed class JwtCallbackAuthenticator(
    string issuer,
    string audience,
    IConfigurationManager<OpenIdConnectConfiguration> keys)
{
    /// <summary>Maximum accepted token lifetime; provider callback tokens are minutes long.</summary>
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromMinutes(15);

    private const int MaxTokenLength = 8 * 1024;
    private static readonly JsonWebTokenHandler Handler = new();

    /// <summary>True only for a single valid, short-lived RS256 bearer token from the configured issuer for this audience.</summary>
    public async Task<bool> AuthenticateAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (authorizationHeader is null || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (token.Length is 0 or > MaxTokenLength) return false;

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await keys.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Unavailable key metadata never authenticates anyone.
            return false;
        }

        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt) return false;

        var start = jwt.IssuedAt != DateTime.MinValue ? jwt.IssuedAt : jwt.ValidFrom;
        return start != DateTime.MinValue && jwt.ValidTo - start <= MaxTokenLifetime;
    }
}
