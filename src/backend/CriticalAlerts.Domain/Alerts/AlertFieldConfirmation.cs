namespace CriticalAlerts.Domain.Alerts;

public sealed class AlertFieldConfirmation
{
    private AlertFieldConfirmation()
    {
        FieldId = string.Empty;
        OriginalValue = string.Empty;
        NormalizedValue = string.Empty;
        Unit = string.Empty;
    }

    internal AlertFieldConfirmation(
        AlertFieldConfirmationId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        string fieldId,
        string originalValue,
        string normalizedValue,
        string? unit,
        FieldConfirmationStatus status,
        UserId confirmedByUserId,
        DateTimeOffset confirmedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        FieldId = fieldId;
        OriginalValue = originalValue;
        NormalizedValue = normalizedValue;
        Unit = unit;
        Status = status;
        ConfirmedByUserId = confirmedByUserId;
        ConfirmedAtUtc = confirmedAtUtc;
    }

    public AlertFieldConfirmationId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertDraftVersion AlertVersion { get; private set; }

    public string FieldId { get; private set; }

    public string OriginalValue { get; private set; }

    public string NormalizedValue { get; private set; }

    public string? Unit { get; private set; }

    public FieldConfirmationStatus Status { get; private set; }

    public UserId ConfirmedByUserId { get; private set; }

    public DateTimeOffset ConfirmedAtUtc { get; private set; }

    internal void ReplaceCanonical(
        string originalValue,
        string normalizedValue,
        string? unit,
        FieldConfirmationStatus status,
        UserId confirmedByUserId,
        DateTimeOffset confirmedAtUtc)
    {
        OriginalValue = originalValue;
        NormalizedValue = normalizedValue;
        Unit = unit;
        Status = status;
        ConfirmedByUserId = confirmedByUserId;
        ConfirmedAtUtc = confirmedAtUtc;
    }
}
