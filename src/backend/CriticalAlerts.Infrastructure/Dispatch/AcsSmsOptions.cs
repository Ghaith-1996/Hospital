using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace CriticalAlerts.Infrastructure.Dispatch;

/// <summary>
/// Validated Azure Communication Services SMS settings. Only explicitly mapped test numbers can be texted;
/// directory contact values are never read. Error messages never echo configured values.
/// </summary>
public sealed partial class AcsSmsOptions
{
    public const string ProviderName = "AzureCommunicationServices";
    public const string SimulationProviderName = "Simulation";
    public const string ConfigurationSection = "Communications:Sms";

    private static readonly HashSet<string> AllowedEnvironments = new(StringComparer.Ordinal) { "Development", "Test", "Staging" };

    private AcsSmsOptions(
        Uri endpoint,
        byte[] accessKey,
        string fromNumber,
        IReadOnlyDictionary<string, string> testRecipients,
        TimeSpan deliveryReportWindow,
        TimeSpan uncertainOutcomeWindow)
    {
        Endpoint = endpoint;
        AccessKey = accessKey;
        FromNumber = fromNumber;
        TestRecipients = testRecipients;
        DeliveryReportWindow = deliveryReportWindow;
        UncertainOutcomeWindow = uncertainOutcomeWindow;
    }

    public Uri Endpoint { get; }

    internal byte[] AccessKey { get; }

    internal string FromNumber { get; }

    internal IReadOnlyDictionary<string, string> TestRecipients { get; }

    /// <summary>DEMO technical bound for waiting on a delivery report; not a clinical or production service level.</summary>
    public TimeSpan DeliveryReportWindow { get; }

    /// <summary>DEMO technical bound for same-key resubmission after an ambiguous provider outcome.</summary>
    public TimeSpan UncertainOutcomeWindow { get; }

    /// <summary>Returns null when the SMS provider is the default simulation.</summary>
    public static AcsSmsOptions? FromConfiguration(IConfiguration configuration, string environmentName)
    {
        var section = configuration.GetSection(ConfigurationSection);
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider) || provider == SimulationProviderName) return null;
        if (provider != ProviderName)
            throw new InvalidOperationException("Communications:Sms:Provider must be Simulation or AzureCommunicationServices.");

        var acs = section.GetSection(ProviderName);
        var recipients = acs.GetSection("TestRecipients").GetChildren()
            .ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal);
        return Create(
            environmentName,
            acs["Endpoint"],
            acs["AccessKey"],
            acs["FromNumber"],
            recipients,
            ParseSeconds(acs["DeliveryReportWindowSeconds"]),
            ParseSeconds(acs["UncertainOutcomeWindowSeconds"]));
    }

    public static AcsSmsOptions Create(
        string environmentName,
        string? endpoint,
        string? accessKey,
        string? fromNumber,
        IReadOnlyDictionary<string, string> testRecipients,
        int? deliveryReportWindowSeconds,
        int? uncertainOutcomeWindowSeconds)
    {
        if (!AllowedEnvironments.Contains(environmentName))
            throw new InvalidOperationException(
                "Azure Communication Services SMS is limited to Development, Test and Staging. Production enablement is REQUIRES_HOSPITAL_DECISION.");

        var uri = ValidateEndpoint(endpoint);
        var key = DecodeKey(accessKey);
        if (fromNumber is null || !E164().IsMatch(fromNumber))
            throw new InvalidOperationException("Communications:Sms:AzureCommunicationServices:FromNumber must be an E.164 number.");
        if (testRecipients.Count is 0 or > 50)
            throw new InvalidOperationException("Communications:Sms:AzureCommunicationServices:TestRecipients must map 1-50 synthetic endpoint labels.");
        foreach (var (label, number) in testRecipients)
        {
            if (!SyntheticLabel().IsMatch(label) || !E164().IsMatch(number))
                throw new InvalidOperationException(
                    "Every TestRecipients entry must map a synthetic SIM- endpoint label to an approved E.164 test number.");
        }

        var delivery = deliveryReportWindowSeconds ?? 300;
        var uncertain = uncertainOutcomeWindowSeconds ?? 120;
        // Same-key replays stay well inside a 5-minute repeatability tracking period (documented by ACS for Email/Rooms;
        // unverified for SMS), with a margin for clock skew. Staging must confirm actual SMS behavior.
        if (delivery is < 60 or > 43200 || uncertain is < 10 or > 240)
            throw new InvalidOperationException("The ACS SMS DEMO windows are outside their allowed ranges.");

        return new AcsSmsOptions(
            uri,
            key,
            fromNumber,
            new Dictionary<string, string>(testRecipients, StringComparer.Ordinal),
            TimeSpan.FromSeconds(delivery),
            TimeSpan.FromSeconds(uncertain));
    }

    private static Uri ValidateEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/"
            || !ResourceHost().IsMatch(uri.Host))
        {
            throw new InvalidOperationException(
                "Communications:Sms:AzureCommunicationServices:Endpoint must be https://<resource>.communication.azure.com.");
        }

        return new Uri($"https://{uri.Host}/", UriKind.Absolute);
    }

    private static byte[] DecodeKey(string? accessKey)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            throw new InvalidOperationException("Communications:Sms:AzureCommunicationServices:AccessKey is required from a secret store.");
        try
        {
            var key = Convert.FromBase64String(accessKey);
            if (key.Length >= 32) return key;
        }
        catch (FormatException)
        {
        }

        throw new InvalidOperationException("Communications:Sms:AzureCommunicationServices:AccessKey is not a valid access key.");
    }

    private static int? ParseSeconds(string? value)
        => value is null ? null
            : int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : throw new InvalidOperationException("ACS SMS window settings must be whole seconds.");

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex E164();

    [GeneratedRegex(@"^SIM-[A-Z0-9][A-Z0-9-]{0,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex SyntheticLabel();

    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?\.communication\.azure\.com$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceHost();
}
