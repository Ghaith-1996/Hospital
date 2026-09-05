namespace CriticalAlerts.Api.Http;

internal static class ApiLimits
{
    // The simulation accepts small JSON commands and CSV previews only.
    public const long MaxRequestBodyBytes = 2 * 1024 * 1024;
}
