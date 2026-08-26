using System.Text.Json;
using CriticalAlerts.Application.Alerts;
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
    TimeProvider time) : IAlertDraftService
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
        var (siteId, departmentId) = await ValidateLocationAsync(organizationId, request.SiteId, request.DepartmentId, cancellationToken);
        var now = time.GetUtcNow();
        var alert = Alert.CreateDraft(
            AlertId.New(),
            organizationId,
            siteId,
            departmentId,
            actorUserId,
            RequireText(request.SimulationPatientReference, "simulation patient reference"),
            location,
            urgency,
            AlertSourceType.Typed,
            protector.Protect(sourceText, Context("alert-typed-source", organizationId)),
            now,
            protector.Protect(JsonSerializer.Serialize(sbar, JsonOptions), Context("alert-sbar", organizationId)));

        foreach (var field in request.CriticalFields ?? [])
        {
            var fieldId = RequireText(field.FieldId, "critical field identifier");
            var originalValue = RequireText(field.OriginalValue, "critical field value");
            alert.RegisterUnresolvedCriticalField(fieldId, originalValue, field.Unit, alert.DraftVersion);
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
        var now = time.GetUtcNow();
        alert.UpdateTypedContent(
            location,
            urgency,
            protector.Protect(sourceText, Context("alert-typed-source", organizationId)),
            protector.Protect(JsonSerializer.Serialize(sbar, JsonOptions), Context("alert-sbar", organizationId)),
            new AlertDraftVersion(request.ExpectedVersion),
            now);

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

    private async Task<Alert?> LoadAsync(OrganizationId organizationId, AlertId alertId, CancellationToken cancellationToken)
        => await db.Alerts
            .Include(alert => alert.FieldConfirmations)
            .Include(alert => alert.StateTransitions)
            .SingleOrDefaultAsync(alert => alert.OrganizationId == organizationId && alert.Id == alertId, cancellationToken);

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

    private static SensitiveDataContext Context(string purpose, OrganizationId organizationId)
        => new(purpose, organizationId.Value);

    private void AddAudit(OrganizationId organizationId, UserId actorUserId, string correlationId, Guid alertId, string action, DateTimeOffset occurredAtUtc)
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
            "{\"simulationOnly\":true}",
            occurredAtUtc));

    private AlertDraftView ToView(Alert alert, OrganizationId organizationId)
    {
        var sourceText = alert.OriginalSource is null
            ? null
            : protector.Unprotect(alert.OriginalSource, Context("alert-typed-source", organizationId));
        var sbar = alert.StructuredSuggestion is null
            ? null
            : JsonSerializer.Deserialize<AlertSbarDraft>(
                protector.Unprotect(alert.StructuredSuggestion, Context("alert-sbar", organizationId)),
                JsonOptions);
        return new AlertDraftView(
            alert.Id.Value,
            alert.State.ToString(),
            alert.DraftVersion.Value,
            alert.SimulationPatientReference,
            alert.Location,
            alert.UrgencyLabel,
            alert.SourceType.ToString(),
            sourceText,
            sbar,
            alert.FieldConfirmations
                .Where(confirmation => confirmation.AlertVersion == alert.DraftVersion)
                .OrderBy(confirmation => confirmation.FieldId)
                .Select(confirmation => new AlertFieldConfirmationView(
                    confirmation.FieldId,
                    confirmation.OriginalValue,
                    confirmation.NormalizedValue,
                    confirmation.Unit,
                    confirmation.Status.ToString(),
                    confirmation.ConfirmedByUserId.Value,
                    confirmation.ConfirmedAtUtc))
                .ToArray());
    }
}
