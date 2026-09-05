using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Alerts;

public sealed class AlertDraftService(
    CriticalAlertsDbContext db,
    ISensitiveDataProtector protector,
    TimeProvider time,
    IDirectorySelectionResolver directorySelectionResolver) : IAlertDraftService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AlertDraftView> CreateAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        CreateAlertDraftRequest request,
        CancellationToken cancellationToken)
    {
        var location = RequireText(request.Location, "location");
        var urgency = RequireText(request.UrgencyLabel, "urgency");
        var sourceText = RequireSimulationText(request.SourceText, "typed source");
        var sbar = ValidateSbar(request.Sbar);
        var criticalFields = ValidateCriticalFields(request.CriticalFields, required: false);
        var (siteId, departmentId) = await ValidateLocationAsync(organizationId, request.SiteId, request.DepartmentId, cancellationToken);
        var now = time.GetUtcNow();
        var alert = Alert.CreateDraft(
            AlertId.New(),
            organizationId,
            siteId,
            departmentId,
            actorUserId,
            RequireText(request.SimulationPatientReference, "simulation patient reference"),
            protector.Protect(
                RequireText(request.SimulationPatientReference, "simulation patient reference"),
                Context(ProtectedValuePurposes.AlertPatientReference, organizationId)),
            location,
            urgency,
            AlertSourceType.Typed,
            protector.Protect(sourceText, Context(ProtectedValuePurposes.AlertTypedSource, organizationId)),
            now,
            protector.Protect(JsonSerializer.Serialize(sbar, JsonOptions), Context(ProtectedValuePurposes.AlertSbar, organizationId)));

        foreach (var field in criticalFields)
        {
            alert.RegisterUnresolvedCriticalField(field.FieldId, field.OriginalValue, field.Unit, alert.DraftVersion);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Alerts.Add(alert);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.draft.created", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        return alert is null ? null : ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> UpdateAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        UpdateAlertDraftRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var location = RequireText(request.Location, "location");
        var urgency = RequireText(request.UrgencyLabel, "urgency");
        var sourceText = RequireSimulationText(request.SourceText, "typed source");
        var sbar = ValidateSbar(request.Sbar);
        var criticalFields = ValidateCriticalFields(request.CriticalFields, required: true);
        var now = time.GetUtcNow();
        alert.UpdateTypedContent(
            location,
            urgency,
            protector.Protect(sourceText, Context(ProtectedValuePurposes.AlertTypedSource, organizationId)),
            protector.Protect(JsonSerializer.Serialize(sbar, JsonOptions), Context(ProtectedValuePurposes.AlertSbar, organizationId)),
            new AlertDraftVersion(request.ExpectedVersion),
            now,
            actorUserId);
        foreach (var field in criticalFields)
        {
            alert.RegisterUnresolvedCriticalField(field.FieldId, field.OriginalValue, field.Unit, alert.DraftVersion);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.draft.updated", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> ConfirmCriticalFieldAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ConfirmAlertCriticalFieldRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        alert.ConfirmCriticalField(
            RequireText(request.FieldId, "critical field identifier"),
            RequireText(request.OriginalValue, "critical field value"),
            RequireText(request.NormalizedValue, "normalized critical field value"),
            request.Unit,
            actorUserId,
            new AlertDraftVersion(request.ExpectedVersion),
            now);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.critical-field.confirmed", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> SubmitAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        SubmitAlertDraftRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        alert.SubmitForConfirmation(actorUserId, new AlertDraftVersion(request.ExpectedVersion), now);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.draft.submitted", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> SetApprovedMessageAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        SetApprovedMessageRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var approvedMessage = RequireSimulationText(request.ApprovedMessage, "approved message");
        var now = time.GetUtcNow();
        alert.SetApprovedMessage(
            protector.Protect(approvedMessage, Context(ProtectedValuePurposes.AlertApprovedMessage, organizationId)),
            new AlertDraftVersion(request.ExpectedVersion),
            now);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.approved-message.updated", now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    public async Task<AlertDraftView?> ReplaceRecipientsAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ReplaceAlertRecipientsRequest request,
        CancellationToken cancellationToken)
    {
        var alert = await LoadAsync(organizationId, alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        if (request.Recipients is null)
        {
            throw new AlertDraftValidationException(
                "recipients-required",
                "The complete recipient list is required; use an empty list to clear it.");
        }

        var candidates = ParseRecipientCandidates(request.Recipients);
        var now = time.GetUtcNow();
        var validated = await directorySelectionResolver.ResolveAsync(
            organizationId,
            candidates,
            now,
            cancellationToken);
        alert.ReplaceRecipients(
            validated,
            actorUserId,
            new AlertDraftVersion(request.ExpectedVersion),
            now);

        var metadata = JsonSerializer.Serialize(new
        {
            simulationOnly = true,
            version = alert.DraftVersion.Value,
            recipientCount = alert.CurrentRecipients.Count,
            channels = alert.CurrentRecipients
                .Select(recipient => recipient.Channel.ToString())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(channel => channel, StringComparer.Ordinal)
                .ToArray(),
            escalationPolicyVersion = alert.DemoEscalationPolicyVersion,
            notificationPolicyVersion = alert.DemoNotificationPolicyVersion,
        }, JsonOptions);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(organizationId, actorUserId, correlationId, alert.Id.Value, "alert.recipients.replaced", now, metadata);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToView(alert, organizationId);
    }

    private async Task<Alert?> LoadAsync(OrganizationId organizationId, AlertId alertId, CancellationToken cancellationToken)
        => await db.Alerts
            .Include(alert => alert.FieldConfirmations)
            .Include(alert => alert.RecipientSelections)
            .Include(alert => alert.StateTransitions)
            .Include(alert => alert.SourceRevisions)
            .SingleOrDefaultAsync(alert => alert.OrganizationId == organizationId && alert.Id == alertId, cancellationToken);

    private static IReadOnlyList<DirectorySelectionCandidate> ParseRecipientCandidates(
        IReadOnlyList<AlertRecipientInput> inputs)
    {
        var candidates = new List<DirectorySelectionCandidate>(inputs.Count);
        foreach (var input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (string.IsNullOrWhiteSpace(input.Channel))
            {
                throw new AlertDraftValidationException("recipient-channel-required", "Each recipient requires an allowed channel.");
            }

            if (string.IsNullOrWhiteSpace(input.DirectoryRevision))
            {
                throw new AlertDraftValidationException("directory-revision-required", "Each recipient requires the reviewed directory revision.");
            }

            candidates.Add(new DirectorySelectionCandidate(
                new PractitionerId(input.PractitionerId),
                input.PractitionerRoleId is Guid roleId ? new PractitionerRoleId(roleId) : null,
                ParseNotificationChannel(input.Channel),
                input.DirectoryRevision.Trim()));
        }

        return candidates;
    }

    private static NotificationChannel ParseNotificationChannel(string value)
        => value.Trim() switch
        {
            "SecureMessage" => NotificationChannel.SecureMessage,
            "Sms" => NotificationChannel.Sms,
            "Voice" => NotificationChannel.Voice,
            _ => throw new AlertDraftValidationException(
                "invalid-recipient-channel",
                "Each recipient requires an allowed channel."),
        };

    private async Task<(SiteId SiteId, DepartmentId DepartmentId)> ValidateLocationAsync(
        OrganizationId organizationId,
        Guid siteValue,
        Guid departmentValue,
        CancellationToken cancellationToken)
    {
        var siteId = new SiteId(siteValue);
        var departmentId = new DepartmentId(departmentValue);
        var valid = await db.Departments.AnyAsync(
            department => department.OrganizationId == organizationId
                && department.Id == departmentId
                && department.SiteId == siteId,
            cancellationToken);
        if (!valid)
        {
            throw new AlertDraftValidationException("invalid-location", "The selected simulation site and department are not valid for this organization.");
        }

        return (siteId, departmentId);
    }

    private static AlertSbarDraft ValidateSbar(AlertSbarDraft? sbar)
    {
        if (sbar is null)
        {
            throw new AlertDraftValidationException("sbar-required", "Situation, background, assessment, and recommendation are required.");
        }

        return new AlertSbarDraft(
            RequireSimulationText(sbar.Situation, "situation"),
            RequireSimulationText(sbar.Background, "background"),
            RequireSimulationText(sbar.Assessment, "assessment"),
            RequireSimulationText(sbar.Recommendation, "recommendation"));
    }

    private static string RequireSimulationText(string? value, string field)
    {
        var text = RequireText(value, field);
        if (!text.StartsWith("SIMULATION:", StringComparison.OrdinalIgnoreCase))
        {
            throw new AlertDraftValidationException("non-simulation-content", $"The {field} must be marked as simulation content.");
        }

        return text;
    }

    private static string RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AlertDraftValidationException("required-field", $"The {field} is required.");
        }

        return value.Trim();
    }

    private static IReadOnlyList<ValidatedCriticalField> ValidateCriticalFields(
        IReadOnlyList<AlertCriticalFieldInput>? fields,
        bool required)
    {
        if (fields is null)
        {
            if (required)
            {
                throw new AlertDraftValidationException(
                    "critical-fields-required",
                    "The complete critical-field list is required when editing a draft.");
            }

            return [];
        }

        var validated = new List<ValidatedCriticalField>(fields.Count);
        var fieldIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var fieldId = RequireText(field.FieldId, "critical field identifier");
            if (!fieldIds.Add(fieldId))
            {
                throw new AlertDraftValidationException(
                    "duplicate-critical-field",
                    "Each critical field identifier may appear only once in a draft version.");
            }

            validated.Add(new ValidatedCriticalField(
                fieldId,
                RequireText(field.OriginalValue, "critical field value"),
                string.IsNullOrWhiteSpace(field.Unit) ? null : field.Unit.Trim()));
        }

        return validated;
    }

    private static SensitiveDataContext Context(string purpose, OrganizationId organizationId)
        => new(purpose, organizationId.Value);

    private void AddAudit(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        Guid alertId,
        string action,
        DateTimeOffset occurredAtUtc,
        string? sanitizedMetadata = null)
        => db.AuditEvents.Add(AuditEvent.Record(
            AuditEventId.New(),
            organizationId,
            "user",
            actorUserId,
            action,
            "alert",
            alertId,
            "succeeded",
            correlationId,
            sanitizedMetadata ?? "{\"simulationOnly\":true}",
            occurredAtUtc));

    private AlertDraftView ToView(Alert alert, OrganizationId organizationId)
    {
        var source = alert.CurrentSourceRevision?.Source ?? alert.OriginalSource;
        var sourceText = source is null
            ? null
            : protector.Unprotect(source, Context(ProtectedValuePurposes.AlertTypedSource, organizationId));
        var sbar = alert.StructuredSuggestion is null
            ? null
            : JsonSerializer.Deserialize<AlertSbarDraft>(
                protector.Unprotect(alert.StructuredSuggestion, Context(ProtectedValuePurposes.AlertSbar, organizationId)),
                JsonOptions);
        var approvedMessage = alert.ApprovedMessage is null
            ? null
            : protector.Unprotect(alert.ApprovedMessage, Context(ProtectedValuePurposes.AlertApprovedMessage, organizationId));
        return new AlertDraftView(
            alert.Id.Value,
            alert.State.ToString(),
            alert.DraftVersion.Value,
            protector.Unprotect(
                alert.SimulationPatientReference,
                Context(ProtectedValuePurposes.AlertPatientReference, organizationId)),
            alert.Location,
            alert.UrgencyLabel,
            alert.SourceType.ToString(),
            sourceText,
            sbar,
            alert.FieldConfirmations
                .Where(confirmation => confirmation.AlertVersion == alert.DraftVersion)
                .OrderBy(confirmation => confirmation.FieldId)
                .Select(confirmation => new AlertFieldConfirmationView(
                    confirmation.AlertVersion.Value,
                    confirmation.FieldId,
                    confirmation.OriginalValue,
                    confirmation.NormalizedValue,
                    confirmation.Unit,
                    confirmation.Status.ToString(),
                    confirmation.ConfirmedByUserId.Value,
                    confirmation.ConfirmedAtUtc))
                .ToArray(),
            approvedMessage,
            alert.CurrentRecipients
                .OrderBy(recipient => recipient.PractitionerId.Value)
                .ThenBy(recipient => recipient.Channel)
                .Select(recipient => new AlertRecipientSelectionView(
                    recipient.PractitionerId.Value,
                    recipient.PractitionerRoleId?.Value,
                    recipient.Channel.ToString(),
                    recipient.SelectedAtUtc,
                    recipient.DirectoryRevision,
                    recipient.DirectorySourceUpdatedAtUtc,
                    recipient.OnCallSnapshot,
                    recipient.SelectionSource.ToString()))
                .ToArray());
    }

    private sealed record ValidatedCriticalField(string FieldId, string OriginalValue, string? Unit);
}
