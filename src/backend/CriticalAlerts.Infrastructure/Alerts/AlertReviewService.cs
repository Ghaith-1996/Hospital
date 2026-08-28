using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Alerts;

public sealed class AlertReviewService(
    CriticalAlertsDbContext db,
    ISensitiveDataProtector protector) : IAlertReviewService
{
    public async Task<AlertReviewView?> GetAsync(
        OrganizationId organizationId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        var alert = await db.Alerts
            .AsNoTracking()
            .Include(candidate => candidate.FieldConfirmations)
            .Include(candidate => candidate.RecipientSelections)
            .SingleOrDefaultAsync(
                candidate => candidate.OrganizationId == organizationId && candidate.Id == alertId,
                cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var fields = alert.FieldConfirmations
            .Where(field => field.AlertVersion == alert.DraftVersion)
            .OrderBy(field => field.FieldId, StringComparer.Ordinal)
            .ToArray();
        var recipients = alert.CurrentRecipients
            .OrderBy(recipient => recipient.PractitionerId.Value)
            .ThenBy(recipient => recipient.Channel)
            .ToArray();
        if (alert.State != AlertState.PendingConfirmation
            || alert.ApprovedMessage is null
            || alert.ApprovedMessage.Ciphertext.Length == 0
            || recipients.Length == 0
            || fields.Any(field => field.Status != FieldConfirmationStatus.Confirmed))
        {
            throw NotReady();
        }

        var practitionerIds = recipients.Select(recipient => recipient.PractitionerId).Distinct().ToArray();
        var practitioners = await db.Practitioners
            .AsNoTracking()
            .Where(practitioner => practitioner.OrganizationId == organizationId && practitionerIds.Contains(practitioner.Id))
            .ToDictionaryAsync(practitioner => practitioner.Id, cancellationToken);
        if (practitioners.Count != practitionerIds.Length || practitioners.Values.Any(practitioner => !practitioner.IsActive))
        {
            throw NotReady();
        }

        var roles = await db.PractitionerRoles
            .AsNoTracking()
            .Where(role => role.OrganizationId == organizationId && practitionerIds.Contains(role.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var selectedRoleIds = recipients
            .Where(recipient => recipient.PractitionerRoleId is not null)
            .Select(recipient => recipient.PractitionerRoleId!.Value)
            .Distinct()
            .ToArray();
        if (selectedRoleIds.Any(roleId => roles.All(role => role.Id != roleId)))
        {
            throw NotReady();
        }

        var departmentIds = roles.Select(role => role.DepartmentId).Distinct().ToArray();
        var departments = await db.Departments
            .AsNoTracking()
            .Where(department => department.OrganizationId == organizationId && departmentIds.Contains(department.Id))
            .ToDictionaryAsync(department => department.Id, cancellationToken);
        var siteIds = departments.Values.Select(department => department.SiteId).Distinct().ToArray();
        var sites = await db.Sites
            .AsNoTracking()
            .Where(site => site.OrganizationId == organizationId && siteIds.Contains(site.Id))
            .ToDictionaryAsync(site => site.Id, cancellationToken);
        var sourceRecords = (await db.DirectorySourceRecords
                .AsNoTracking()
                .Where(record => record.OrganizationId == organizationId)
                .ToArrayAsync(cancellationToken))
            .Where(record => record.PractitionerId is PractitionerId practitionerId && practitionerIds.Contains(practitionerId))
            .ToArray();

        var reviewRecipients = recipients.Select(recipient =>
        {
            var practitioner = practitioners[recipient.PractitionerId];
            var practitionerRoles = roles
                .Where(role => role.PractitionerId == practitioner.Id)
                .OrderByDescending(role => role.IsPrimary)
                .ThenBy(role => role.Title, StringComparer.Ordinal)
                .ThenBy(role => role.Id.Value)
                .ToArray();
            var role = recipient.PractitionerRoleId is PractitionerRoleId requestedRole
                ? practitionerRoles.SingleOrDefault(candidate => candidate.Id == requestedRole)
                : practitionerRoles.FirstOrDefault();
            if (recipient.PractitionerRoleId is not null && role is null)
            {
                throw NotReady();
            }

            var department = role is null ? null : departments.GetValueOrDefault(role.DepartmentId);
            var site = department is null ? null : sites.GetValueOrDefault(department.SiteId);
            var source = sourceRecords
                .Where(record => record.PractitionerId == practitioner.Id)
                .Where(record => recipient.DirectorySourceUpdatedAtUtc is null
                    || record.SourceUpdatedAtUtc == recipient.DirectorySourceUpdatedAtUtc)
                .OrderByDescending(record => record.LastSeenAtUtc)
                .FirstOrDefault();

            return new AlertReviewRecipient(
                practitioner.Id.Value,
                $"{practitioner.FirstName} {practitioner.LastName}",
                practitioner.Specialty,
                department?.Name,
                site?.Name,
                role?.Title,
                recipient.Channel.ToString(),
                recipient.SelectedAtUtc,
                recipient.DirectorySourceUpdatedAtUtc,
                recipient.OnCallSnapshot,
                source?.IsStale ?? false,
                recipient.DirectoryRevision);
        }).ToArray();

        return new AlertReviewView(
            alert.Id.Value,
            alert.DraftVersion.Value,
            alert.State.ToString(),
            alert.SimulationPatientReference,
            alert.Location,
            alert.UrgencyLabel,
            protector.Unprotect(alert.ApprovedMessage, new SensitiveDataContext("alert-approved-message", organizationId.Value)),
            fields.Select(field => new AlertReviewCriticalField(
                    field.AlertVersion.Value,
                    field.FieldId,
                    field.OriginalValue,
                    field.NormalizedValue,
                    field.Unit,
                    field.Status.ToString(),
                    field.ConfirmedByUserId.Value,
                    field.ConfirmedAtUtc))
                .ToArray(),
            reviewRecipients,
            alert.DemoEscalationPolicyVersion,
            alert.DemoNotificationPolicyVersion);
    }

    private static AlertReviewValidationException NotReady()
        => new(
            "review-not-ready",
            "The alert changed or is not complete for exact review. Reload the alert and confirm the current version.");
}
