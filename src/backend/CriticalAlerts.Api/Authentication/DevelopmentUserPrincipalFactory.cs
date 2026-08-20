using System.Security.Claims;
using CriticalAlerts.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace CriticalAlerts.Api.Authentication;

internal static class DevelopmentUserPrincipalFactory
{
    public static ClaimsPrincipal Create(SeededIdentity identity)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId.ToString("D")),
            new(ClaimTypes.Name, identity.DisplayName),
            new(AuthenticationClaimTypes.OrganizationId, identity.OrganizationId.ToString("D")),
            new(AuthenticationClaimTypes.SimulationHandle, identity.SimulationHandle),
            new(AuthenticationClaimTypes.AuthenticationMode, AuthenticationClaimTypes.DevelopmentMode),
        };

        claims.AddRange(identity.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
