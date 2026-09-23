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
    string DirectoryRevision,
    string SelectionSource);

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
    string DemoNotificationPolicyVersion,
    EscalationPlanView? EscalationPlan = null);

public sealed record EscalationPlanRecipient(
    Guid PractitionerId,
    Guid? PractitionerRoleId,
    string DisplayName,
    string Specialty,
    string? Department,
    string? Site,
    string? RoleTitle,
    string Channel,
    string DirectoryRevision,
    DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string? OnCallSnapshot,
    EscalationOnCallEvidence? OnCallEvidence = null);

public sealed record EscalationOnCallEvidence(
    Guid AssignmentId,
    string SourceSystem,
    string SourceRecordId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    DateTimeOffset LastSynchronizedAtUtc);

public sealed record EscalationPlanStepView(
    Guid StepId,
    int SequenceNumber,
    long DelaySeconds,
    IReadOnlyList<EscalationPlanRecipient> Recipients);

public sealed record EscalationPlanView(
    Guid PolicyId,
    string PolicyVersion,
    string Revision,
    IReadOnlyList<EscalationPlanStepView> Steps);

public sealed record ConfirmAlertReviewRequest(
    int ExpectedVersion,
    Guid? EscalationPolicyId = null,
    string? EscalationPolicyVersion = null,
    string? EscalationPlanRevision = null);

public sealed record ConfirmAlertReviewResult(
    Guid AlertId,
    int ConfirmedVersion,
    string State,
    bool Replayed);

public sealed class AlertReviewValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}

public sealed class AlertConfirmationValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}

public interface IAlertReviewService
{
    Task<AlertReviewView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken);

    Task<ConfirmAlertReviewResult?> ConfirmAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ConfirmAlertReviewRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken);
}
