namespace CriticalAlerts.Domain.Alerts;

public sealed record ValidatedRecipientSelection(
    PractitionerId PractitionerId,
    PractitionerRoleId? PractitionerRoleId,
    NotificationChannel Channel,
    string DirectoryRevision,
    DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string? OnCallSnapshot);

public sealed class AlertRecipientSelection
{
    private AlertRecipientSelection()
    {
        DirectoryRevision = string.Empty;
    }

    public AlertRecipientSelection(
        AlertRecipientSelectionId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        PractitionerId practitionerId,
        PractitionerRoleId? practitionerRoleId,
        NotificationChannel channel,
        UserId selectedByUserId,
        DateTimeOffset selectedAtUtc,
        string directoryRevision,
        DateTimeOffset? directorySourceUpdatedAtUtc,
        string? onCallSnapshot)
    {
        ValidateDirectoryRevision(directoryRevision);
        ValidateOnCallSnapshot(onCallSnapshot);
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        PractitionerId = practitionerId;
        PractitionerRoleId = practitionerRoleId;
        Channel = channel;
        SelectedByUserId = selectedByUserId;
        SelectedAtUtc = UtcInstant.Require(selectedAtUtc, nameof(selectedAtUtc));
        DirectoryRevision = directoryRevision;
        DirectorySourceUpdatedAtUtc = directorySourceUpdatedAtUtc is null
            ? null
            : UtcInstant.Require(directorySourceUpdatedAtUtc.Value, nameof(directorySourceUpdatedAtUtc));
        OnCallSnapshot = onCallSnapshot;
    }

    public AlertRecipientSelectionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertDraftVersion AlertVersion { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public PractitionerRoleId? PractitionerRoleId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public UserId SelectedByUserId { get; private set; }

    public DateTimeOffset SelectedAtUtc { get; private set; }

    public string DirectoryRevision { get; private set; }

    public DateTimeOffset? DirectorySourceUpdatedAtUtc { get; private set; }

    public string? OnCallSnapshot { get; private set; }

    internal static void ValidateDirectoryRevision(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new DomainException("Recipient selections require a safe directory revision.");
        }
    }

    internal static void ValidateOnCallSnapshot(string? value)
    {
        if (value is not null && (value.Length > 80 || value.Any(char.IsControl)))
        {
            throw new DomainException("Recipient selections require a safe on-call snapshot.");
        }
    }
}
