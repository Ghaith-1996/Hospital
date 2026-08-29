using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Alerts;

public sealed record AlertSbarDraft(
    string? Situation,
    string? Background,
    string? Assessment,
    string? Recommendation);

public sealed record AlertCriticalFieldInput(
    string? FieldId,
    string? OriginalValue,
    string? Unit);

public sealed record CreateAlertDraftRequest(
    Guid SiteId,
    Guid DepartmentId,
    string? SimulationPatientReference,
    string? Location,
    string? UrgencyLabel,
    string? SourceText,
    AlertSbarDraft? Sbar,
    IReadOnlyList<AlertCriticalFieldInput>? CriticalFields);

public sealed record UpdateAlertDraftRequest(
    int ExpectedVersion,
    string? Location,
    string? UrgencyLabel,
    string? SourceText,
    AlertSbarDraft? Sbar,
    IReadOnlyList<AlertCriticalFieldInput>? CriticalFields);

public sealed record ConfirmAlertCriticalFieldRequest(
    int ExpectedVersion,
    string? FieldId,
    string? OriginalValue,
    string? NormalizedValue,
    string? Unit);

public sealed record SubmitAlertDraftRequest(int ExpectedVersion);

public sealed record SetApprovedMessageRequest(int ExpectedVersion, string? ApprovedMessage);

public sealed record AlertRecipientInput(
    Guid PractitionerId,
    Guid? PractitionerRoleId,
    string? Channel,
    string? DirectoryRevision);

public sealed record ReplaceAlertRecipientsRequest(
    int ExpectedVersion,
    IReadOnlyList<AlertRecipientInput>? Recipients);

public sealed record AlertFieldConfirmationView(
    int AlertVersion,
    string FieldId,
    string OriginalValue,
    string NormalizedValue,
    string? Unit,
    string Status,
    Guid ConfirmedByUserId,
    DateTimeOffset ConfirmedAtUtc);

public sealed record AlertRecipientSelectionView(
    Guid PractitionerId,
    Guid? PractitionerRoleId,
    string Channel,
    DateTimeOffset SelectedAtUtc,
    string DirectoryRevision,
    DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string? OnCallSnapshot);

public sealed record AlertDraftView(
    Guid AlertId,
    string State,
    int DraftVersion,
    string SimulationPatientReference,
    string Location,
    string UrgencyLabel,
    string SourceType,
    string? SourceText,
    AlertSbarDraft? Sbar,
    IReadOnlyList<AlertFieldConfirmationView> CriticalFields,
    string? ApprovedMessage,
    IReadOnlyList<AlertRecipientSelectionView> Recipients);

public sealed class AlertDraftValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}

public interface IAlertDraftService
{
    Task<AlertDraftView> CreateAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        CreateAlertDraftRequest request,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> UpdateAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        UpdateAlertDraftRequest request,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> ConfirmCriticalFieldAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ConfirmAlertCriticalFieldRequest request,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> SubmitAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        SubmitAlertDraftRequest request,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> SetApprovedMessageAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        SetApprovedMessageRequest request,
        CancellationToken cancellationToken);

    Task<AlertDraftView?> ReplaceRecipientsAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ReplaceAlertRecipientsRequest request,
        CancellationToken cancellationToken);
}
