namespace CriticalAlerts.Domain.Assistance;

public enum AssistanceKind { Transcription, Structuring }

/// <summary>Immutable protected evidence. It has no authority to mutate an alert.</summary>
public sealed class AssistanceResult
{
    private AssistanceResult() { }
    public Guid Id { get; private set; }
    public OrganizationId OrganizationId { get; private set; }
    public AlertId AlertId { get; private set; }
    public AlertDraftVersion AlertVersion { get; private set; }
    public AlertSourceRevisionId SourceRevisionId { get; private set; }
    public UserId RequestedByUserId { get; private set; }
    public AssistanceKind Kind { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ProviderVersion { get; private set; } = string.Empty;
    public string ConfigurationVersion { get; private set; } = string.Empty;
    public ProtectedValue Payload { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static string PurposeFor(AssistanceKind kind) => kind switch
    {
        AssistanceKind.Transcription => "alert-transcription-result",
        AssistanceKind.Structuring => "alert-structuring-suggestion",
        _ => throw new DomainException("Unknown assistance result kind."),
    };

    public static AssistanceResult Create(Guid id, OrganizationId organizationId, AlertId alertId, AlertDraftVersion version,
        AlertSourceRevisionId sourceRevisionId, UserId actor, AssistanceKind kind, string provider, string providerVersion,
        string configurationVersion, ProtectedValue payload, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty || provider is not ("Simulated" or "AzureSpeech") || string.IsNullOrWhiteSpace(providerVersion)
            || providerVersion.Length > 40 || string.IsNullOrWhiteSpace(configurationVersion) || configurationVersion.Length > 40
            || payload.Purpose != PurposeFor(kind)) throw new DomainException("Invalid assistance provenance.");
        return new AssistanceResult
        {
            Id = id,
            OrganizationId = organizationId,
            AlertId = alertId,
            AlertVersion = version,
            SourceRevisionId = sourceRevisionId,
            RequestedByUserId = actor,
            Kind = kind,
            Provider = provider,
            ProviderVersion = providerVersion,
            ConfigurationVersion = configurationVersion,
            Payload = new ProtectedValue(payload.Ciphertext.ToArray(), payload.KeyVersion, payload.Purpose),
            CreatedAtUtc = UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)),
        };
    }
}
