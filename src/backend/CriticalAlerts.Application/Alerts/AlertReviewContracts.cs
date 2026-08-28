using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Alerts;

public sealed record AlertReviewCriticalField(
    int AlertVersion,
    string FieldId,
    string OriginalValue,
    string NormalizedValue,
    string? Unit,
    string Status,
    Guid ConfirmedByUserId,
    DateTimeOffset ConfirmedAtUtc);

public sealed record AlertReviewRecipient(
    Guid PractitionerId,
    string DisplayName,
    string Specialty,
    string? Department,
    string? Site,
    string? RoleTitle,
    string Channel,
    DateTimeOffset SelectedAtUtc,
    DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string? OnCallSnapshot,
    bool IsStale,
    string DirectoryRevision);

public sealed record AlertReviewView(
    Guid AlertId,
    int DraftVersion,
    string State,
    string SimulationPatientReference,
    string Location,
    string UrgencyLabel,
    string ApprovedMessage,
    IReadOnlyList<AlertReviewCriticalField> CriticalFields,
    IReadOnlyList<AlertReviewRecipient> Recipients,
    string DemoEscalationPolicyVersion,
    string DemoNotificationPolicyVersion);

public sealed class AlertReviewValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}

public interface IAlertReviewService
{
    Task<AlertReviewView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken);
}
