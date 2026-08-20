namespace CriticalAlerts.Domain.Alerts;

public sealed class AlertRecipientSelection
{
    private AlertRecipientSelection()
    {
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
        DateTimeOffset selectedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        PractitionerId = practitionerId;
        PractitionerRoleId = practitionerRoleId;
        Channel = channel;
        SelectedByUserId = selectedByUserId;
        SelectedAtUtc = selectedAtUtc;
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
}
