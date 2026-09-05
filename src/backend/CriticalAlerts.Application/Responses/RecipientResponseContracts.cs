using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Responses;

public sealed record MyAlertSummaryView(
    Guid AlertId,
    int ConfirmedVersion,
    string State,
    string Location,
    string UrgencyLabel,
    DateTimeOffset ConfirmedAtUtc,
    IReadOnlyList<string> Channels,
    string OpenedState,
    DateTimeOffset? AcknowledgedAtUtc,
    string? TerminalDisposition,
    DateTimeOffset? ResponsibilityAcceptedAtUtc,
    DateTimeOffset? CallUnitRequestedAtUtc,
    string? LastResponseReasonCode);

public sealed record MyAlertCriticalFieldView(
    string FieldId,
    string Value,
    string? Unit);

public sealed record MyAlertDetailView(
    Guid AlertId,
    int ConfirmedVersion,
    string State,
    string SimulationPatientReference,
    string Location,
    string UrgencyLabel,
    string ApprovedMessage,
    IReadOnlyList<MyAlertCriticalFieldView> CriticalFields,
    IReadOnlyList<string> Channels,
    string OpenedState,
    DateTimeOffset? SecureMessageOpenedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    string? TerminalDisposition,
    DateTimeOffset? ResponsibilityAcceptedAtUtc,
    DateTimeOffset? CallUnitRequestedAtUtc,
    string? LastResponseReasonCode);

public sealed record OpenRecipientAlertRequest(int ExpectedVersion);

public sealed record OpenedRecipientAlertResult(
    Guid AlertId,
    int ConfirmedVersion,
    DateTimeOffset? SecureMessageOpenedAtUtc,
    bool Replayed);

public sealed record RecordRecipientResponseRequest(
    int ExpectedVersion,
    string? ResponseType,
    string? ReasonCode = null);

public sealed record RecipientResponseResult(
    Guid AlertId,
    int ConfirmedVersion,
    string ResponseType,
    DateTimeOffset? AcknowledgedAtUtc,
    string? TerminalDisposition,
    DateTimeOffset? ResponsibilityAcceptedAtUtc,
    DateTimeOffset? CallUnitRequestedAtUtc,
    string? ReasonCode,
    bool Replayed);

public interface IRecipientInboxService
{
    Task<IReadOnlyList<MyAlertSummaryView>> ListAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken);

    Task<MyAlertDetailView?> GetAsync(
        OrganizationId organizationId,
        UserId userId,
        AlertId alertId,
        CancellationToken cancellationToken);
}

public interface IRecipientResponseService
{
    Task<OpenedRecipientAlertResult?> MarkOpenedAsync(
        OrganizationId organizationId,
        UserId userId,
        string correlationId,
        AlertId alertId,
        OpenRecipientAlertRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);

    Task<RecipientResponseResult?> RecordAsync(
        OrganizationId organizationId,
        UserId userId,
        string correlationId,
        AlertId alertId,
        RecordRecipientResponseRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class RecipientResponseValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}
