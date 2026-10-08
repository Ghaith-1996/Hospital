using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Validated provider-neutral voice settings. Only explicitly mapped test numbers can be dialed; directory contact
/// values are never read. Every window and count is a technical DEMO value, not a clinical or production service level.
/// Error messages never echo configured values.
/// </summary>
public sealed partial class VoiceDispatchOptions
{
    public const string ConfigurationSection = "Communications:Voice";
    public const string SimulationProviderName = "Simulation";
    public const string CallbackPathPrefix = "/api/v1/webhooks/communications/voice/";

    private static readonly HashSet<string> AllowedEnvironments = new(StringComparer.Ordinal) { "Development", "Test", "Staging" };

    private VoiceDispatchOptions(
        string providerName,
        string callerId,
        IReadOnlyDictionary<string, string> testRecipients,
        int repeats,
        int ringTimeoutSeconds,
        TimeSpan callOutcomeWindow,
        TimeSpan uncertainOutcomeWindow,
        Uri callbackUri)
    {
        ProviderName = providerName;
        CallerId = callerId;
        TestRecipients = testRecipients;
        Repeats = repeats;
        RingTimeoutSeconds = ringTimeoutSeconds;
        CallOutcomeWindow = callOutcomeWindow;
        UncertainOutcomeWindow = uncertainOutcomeWindow;
        CallbackUri = callbackUri;
    }

    public string ProviderName { get; }

    internal string CallerId { get; }

    internal IReadOnlyDictionary<string, string> TestRecipients { get; }

    /// <summary>DEMO: how many times the generic wake-up text is spoken before the provider hangs up.</summary>
    public int Repeats { get; }

    /// <summary>DEMO: how long the provider lets the call ring.</summary>
    public int RingTimeoutSeconds { get; }

    /// <summary>DEMO: how long a placed call may wait for a terminal call event before failing visibly.</summary>
    public TimeSpan CallOutcomeWindow { get; }

    /// <summary>DEMO: bound for same-key resubmission after an ambiguous outcome (idempotent providers only).</summary>
    public TimeSpan UncertainOutcomeWindow { get; }

    public Uri CallbackUri { get; }

    /// <summary>Lowercase letters, digits and inner hyphens, at most 24 characters, never the simulation name.</summary>
    public static bool IsValidProviderName(string? value) => value is not null && ProviderNamePattern().IsMatch(value);

    /// <summary>Returns null when the voice provider is the default simulation.</summary>
    public static VoiceDispatchOptions? FromConfiguration(IConfiguration configuration, string environmentName)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider) || provider == SimulationProviderName) return null;
        if (!AllowedEnvironments.Contains(environmentName))
            throw new InvalidOperationException(
                "A real voice provider is limited to Development, Test and Staging. Production enablement is REQUIRES_HOSPITAL_DECISION.");
        if (!IsValidProviderName(provider))
            throw new InvalidOperationException("Communications:Voice:Provider must be Simulation or a registered provider name.");

        var callerId = section["CallerId"];
        if (callerId is null || !E164().IsMatch(callerId))
            throw new InvalidOperationException("Communications:Voice:CallerId must be an E.164 test caller ID.");

        var recipients = section.GetSection("TestRecipients").GetChildren()
            .ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal);
        if (recipients.Count is 0 or > 50)
            throw new InvalidOperationException("Communications:Voice:TestRecipients must map 1-50 synthetic endpoint labels.");
        foreach (var (label, number) in recipients)
        {
            if (!SyntheticLabel().IsMatch(label) || !E164().IsMatch(number))
                throw new InvalidOperationException(
                    "Every voice TestRecipients entry must map a synthetic SIM- endpoint label to an approved E.164 test number.");
        }

        var repeats = ParseWhole(section["Repeats"]) ?? 2;
        var ring = ParseWhole(section["RingTimeoutSeconds"]) ?? 30;
        var outcome = ParseWhole(section["CallOutcomeWindowSeconds"]) ?? 180;
        var uncertain = ParseWhole(section["UncertainOutcomeWindowSeconds"]) ?? 120;
        if (repeats is < 1 or > 3 || ring is < 10 or > 60 || outcome is < 60 or > 1800 || uncertain is < 10 or > 240)
            throw new InvalidOperationException("The voice DEMO counts and windows are outside their allowed ranges.");

        return new VoiceDispatchOptions(
            provider,
            callerId,
            new Dictionary<string, string>(recipients, StringComparer.Ordinal),
            repeats,
            ring,
            TimeSpan.FromSeconds(outcome),
            TimeSpan.FromSeconds(uncertain),
            new Uri(ValidateCallbackBase(section["CallbackBaseUri"]), CallbackPathPrefix + provider));
    }

    private static Uri ValidateCallbackBase(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
        {
            throw new InvalidOperationException("Communications:Voice:CallbackBaseUri must be an https origin with no path or query.");
        }

        return new Uri($"https://{uri.Host}/", UriKind.Absolute);
    }

    private static int? ParseWhole(string? value)
        => value is null ? null
            : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : throw new InvalidOperationException("Voice DEMO counts and windows must be whole numbers.");

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,22}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderNamePattern();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();

    [GeneratedRegex(@"^SIM-[A-Z0-9][A-Z0-9-]{0,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex SyntheticLabel();
}
