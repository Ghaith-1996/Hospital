using CriticalAlerts.Application.Protection;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Responses;

public sealed class RecipientInboxService(
    CriticalAlertsDbContext db,
    PractitionerIdentityResolver identities,
    ISensitiveDataProtector protector) : IRecipientInboxService
{
    public async Task<IReadOnlyList<MyAlertSummaryView>> ListAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        var practitionerId = await RequirePractitionerAsync(organizationId, userId, cancellationToken);
        var alerts = (await db.Alerts
            .AsNoTracking()
            .Where(alert => alert.OrganizationId == organizationId
                && alert.State == AlertState.Active
                && alert.ConfirmedDraftVersion != null)
            .ToArrayAsync(cancellationToken))
            .OrderByDescending(alert => alert.ConfirmedAtUtc)
            .ThenByDescending(alert => alert.Id.Value)
            .ToArray();
        if (alerts.Length == 0)
        {
            return [];
        }

        var alertIds = alerts.Select(alert => alert.Id).ToArray();
        var selections = await db.AlertRecipientSelections
            .AsNoTracking()
            .Where(selection => selection.OrganizationId == organizationId
                && selection.PractitionerId == practitionerId
                && alertIds.Contains(selection.AlertId))
            .ToArrayAsync(cancellationToken);
        var addressed = alerts
            .Where(alert => selections.Any(selection =>
                selection.AlertId == alert.Id
                && selection.AlertVersion == alert.ConfirmedDraftVersion!.Value))
            .ToArray();
        if (addressed.Length == 0)
        {
            return [];
        }

        var addressedIds = addressed.Select(alert => alert.Id).ToArray();
        var attempts = await db.DeliveryAttempts
            .AsNoTracking()
            .Where(attempt => attempt.OrganizationId == organizationId && addressedIds.Contains(attempt.AlertId))
            .ToArrayAsync(cancellationToken);
        var responses = await db.RecipientResponses
            .AsNoTracking()
            .Where(response => response.OrganizationId == organizationId
                && response.PractitionerId == practitionerId
                && addressedIds.Contains(response.AlertId))
            .ToArrayAsync(cancellationToken);
        var assignments = await db.ResponsibilityAssignments
            .AsNoTracking()
            .Where(assignment => assignment.OrganizationId == organizationId
                && assignment.PractitionerId == practitionerId
                && addressedIds.Contains(assignment.AlertId))
            .ToArrayAsync(cancellationToken);

        return addressed.Select(alert =>
        {
            var version = alert.ConfirmedDraftVersion!.Value;
            var alertSelections = selections
                .Where(selection => selection.AlertId == alert.Id && selection.AlertVersion == version)
                .ToArray();
            var selectionIds = alertSelections.Select(selection => selection.Id).ToHashSet();
            var alertAttempts = attempts.Where(attempt => selectionIds.Contains(attempt.RecipientSelectionId)).ToArray();
            var alertResponses = responses
                .Where(response => response.AlertId == alert.Id && response.AlertVersion == version)
                .ToArray();
            var acknowledgement = alertResponses.SingleOrDefault(response => response.IsAcknowledgement);
            var terminal = alertResponses.SingleOrDefault(response => response.IsTerminalDisposition);
            var callUnit = alertResponses.SingleOrDefault(response => response.IsCallUnitRequest);
            var lastResponse = alertResponses.OrderByDescending(response => response.OccurredAtUtc).FirstOrDefault();
            var assignment = assignments.SingleOrDefault(item => item.AlertId == alert.Id && item.AlertVersion == version);

            return new MyAlertSummaryView(
                alert.Id.Value,
                version.Value,
                alert.State.ToString(),
                alert.Location,
                alert.UrgencyLabel,
                alert.ConfirmedAtUtc!.Value,
                alertSelections.Select(selection => selection.Channel.ToString()).Distinct().Order().ToArray(),
                OpenedState(alertSelections, alertAttempts).ToString(),
                acknowledgement?.OccurredAtUtc,
                terminal?.ResponseType.ToString(),
                assignment?.AcceptedAtUtc,
                callUnit?.OccurredAtUtc,
                lastResponse?.SanitizedReasonCode);
        }).ToArray();
    }

    public async Task<MyAlertDetailView?> GetAsync(
        OrganizationId organizationId,
        UserId userId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        var practitionerId = await RequirePractitionerAsync(organizationId, userId, cancellationToken);
        var alert = await db.Alerts
            .AsNoTracking()
            .Include(item => item.FieldConfirmations)
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Id == alertId
                && item.State == AlertState.Active
                && item.ConfirmedDraftVersion != null,
                cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var version = alert.ConfirmedDraftVersion!.Value;
        var selections = await db.AlertRecipientSelections
            .AsNoTracking()
            .Where(selection => selection.OrganizationId == organizationId
                && selection.AlertId == alertId
                && selection.AlertVersion == version
                && selection.PractitionerId == practitionerId)
            .OrderBy(selection => selection.Channel)
            .ToArrayAsync(cancellationToken);
        if (selections.Length == 0 || alert.ApprovedMessage is null)
        {
            return null;
        }

        var selectionIds = selections.Select(selection => selection.Id).ToArray();
        var attempts = await db.DeliveryAttempts
            .AsNoTracking()
            .Where(attempt => attempt.OrganizationId == organizationId
                && attempt.AlertId == alertId
                && selectionIds.Contains(attempt.RecipientSelectionId))
            .ToArrayAsync(cancellationToken);
        var responses = await db.RecipientResponses
            .AsNoTracking()
            .Where(response => response.OrganizationId == organizationId
                && response.AlertId == alertId
                && response.AlertVersion == version
                && response.PractitionerId == practitionerId)
            .ToArrayAsync(cancellationToken);
        var assignment = await db.ResponsibilityAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == version
                && item.PractitionerId == practitionerId,
                cancellationToken);
        var acknowledgement = responses.SingleOrDefault(response => response.IsAcknowledgement);
        var terminal = responses.SingleOrDefault(response => response.IsTerminalDisposition);
        var callUnit = responses.SingleOrDefault(response => response.IsCallUnitRequest);
        var lastResponse = responses.OrderByDescending(response => response.OccurredAtUtc).FirstOrDefault();

        return new MyAlertDetailView(
            alert.Id.Value,
            version.Value,
            alert.State.ToString(),
            protector.Unprotect(
                alert.SimulationPatientReference,
                new SensitiveDataContext(ProtectedValuePurposes.AlertPatientReference, organizationId.Value)),
            alert.Location,
            alert.UrgencyLabel,
            protector.Unprotect(
                alert.ApprovedMessage,
                new SensitiveDataContext(ProtectedValuePurposes.AlertApprovedMessage, organizationId.Value)),
            alert.FieldConfirmations
                .Where(field => field.AlertVersion == version && field.Status == FieldConfirmationStatus.Confirmed)
                .OrderBy(field => field.FieldId, StringComparer.Ordinal)
                .Select(field => new MyAlertCriticalFieldView(field.FieldId, field.NormalizedValue, field.Unit))
                .ToArray(),
            selections.Select(selection => selection.Channel.ToString()).Distinct().Order().ToArray(),
            OpenedState(selections, attempts).ToString(),
            attempts
                .Where(attempt => attempt.Channel == NotificationChannel.SecureMessage)
                .Select(attempt => attempt.OpenedAtUtc)
                .Where(value => value is not null)
                .Min(),
            acknowledgement?.OccurredAtUtc,
            terminal?.ResponseType.ToString(),
            assignment?.AcceptedAtUtc,
            callUnit?.OccurredAtUtc,
            lastResponse?.SanitizedReasonCode);
    }

    private async Task<PractitionerId> RequirePractitionerAsync(
        OrganizationId organizationId,
        UserId userId,
        CancellationToken cancellationToken)
    {
        return await identities.ResolveAsync(organizationId, userId, cancellationToken)
            ?? throw new RecipientResponseValidationException(
                "practitioner-link-required",
                "The authenticated practitioner identity is not linked to a directory practitioner.");
    }

    private static ObservationState OpenedState(
        IReadOnlyCollection<AlertRecipientSelection> selections,
        IReadOnlyCollection<DeliveryAttempt> attempts)
    {
        if (selections.All(selection => selection.Channel != NotificationChannel.SecureMessage))
        {
            return ObservationState.NotApplicable;
        }

        if (attempts.Any(attempt =>
                attempt.Channel == NotificationChannel.SecureMessage
                && attempt.OpenedState == ObservationState.Occurred))
        {
            return ObservationState.Occurred;
        }

        return ObservationState.PendingNotObserved;
    }
}
