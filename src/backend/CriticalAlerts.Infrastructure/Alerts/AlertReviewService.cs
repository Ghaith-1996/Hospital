using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Alerts;

public sealed class AlertReviewService(
    CriticalAlertsDbContext db,
    ISensitiveDataProtector protector,
    TimeProvider time) : IAlertReviewService
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

    public async Task<ConfirmAlertReviewResult?> ConfirmAsync(
        OrganizationId organizationId,
        UserId actorUserId,
        string correlationId,
        AlertId alertId,
        ConfirmAlertReviewRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = RequireIdempotencyKey(idempotencyKey);
        var requestHash = ComputeRequestHash(organizationId, alertId, request.ExpectedVersion);
        var now = time.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await db.IdempotencyRecords
                .SingleOrDefaultAsync(record => record.OrganizationId == organizationId
                    && record.OperationType == "confirm-review"
                    && record.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                if (!FixedEquals(existing.RequestHash, requestHash))
                {
                    throw new AlertReviewValidationException(
                        "idempotency-conflict",
                        "The idempotency key was already used for a different confirmation request.");
                }

                if (existing.Status == IdempotencyProcessingStatus.Completed)
                {
                    return DecodeResult(existing.ResultReference, replayed: true);
                }

                throw new AlertReviewValidationException(
                    "confirmation-in-progress",
                    "A confirmation with this idempotency key is already in progress. Retry with the same key.");
            }

            var alert = await db.Alerts
                .Include(candidate => candidate.FieldConfirmations)
                .Include(candidate => candidate.RecipientSelections)
                .Include(candidate => candidate.StateTransitions)
                .SingleOrDefaultAsync(
                    candidate => candidate.OrganizationId == organizationId && candidate.Id == alertId,
                    cancellationToken);
            if (alert is null)
            {
                return null;
            }

            EnsureReviewable(alert);
            var recipientIds = alert.CurrentRecipients
                .Select(recipient => recipient.PractitionerId)
                .Distinct()
                .ToArray();
            var currentPractitioners = await db.Practitioners
                .AsNoTracking()
                .Where(practitioner => practitioner.OrganizationId == organizationId && recipientIds.Contains(practitioner.Id))
                .ToArrayAsync(cancellationToken);
            if (currentPractitioners.Length != recipientIds.Length)
            {
                throw NotReady();
            }

            alert.ConfirmForDispatch(
                actorUserId,
                new AlertDraftVersion(request.ExpectedVersion),
                currentPractitioners,
                now,
                correlationId);

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
            });
            db.AuditEvents.Add(AuditEvent.Record(
                AuditEventId.New(),
                organizationId,
                "user",
                actorUserId,
                "alert.confirmed",
                "alert",
                alert.Id.Value,
                "succeeded",
                correlationId,
                metadata,
                now));

            var result = new ConfirmAlertReviewResult(
                alert.Id.Value,
                alert.DraftVersion.Value,
                AlertState.DispatchQueued.ToString(),
                Replayed: false);
            var idempotency = IdempotencyRecord.Start(
                IdempotencyRecordId.New(),
                organizationId,
                "confirm-review",
                key,
                requestHash,
                now);
            idempotency.Complete(EncodeResult(result));
            db.IdempotencyRecords.Add(idempotency);

            var payload = JsonSerializer.Serialize(new
            {
                alertId = alert.Id.Value,
                draftVersion = alert.DraftVersion.Value,
            });
            db.OutboxMessages.Add(OutboxMessage.Create(
                OutboxMessageId.New(),
                organizationId,
                "AlertDispatchRequested",
                alert.Id.Value,
                payload,
                $"alert-dispatch:{alert.Id.Value:D}:v{alert.DraftVersion.Value}",
                now));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            var concurrent = await db.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(record => record.OrganizationId == organizationId
                    && record.OperationType == "confirm-review"
                    && record.IdempotencyKey == key, cancellationToken);
            if (concurrent?.Status == IdempotencyProcessingStatus.Completed
                && FixedEquals(concurrent.RequestHash, requestHash))
            {
                return DecodeResult(concurrent.ResultReference, replayed: true);
            }

            throw new AlertReviewValidationException(
                "confirmation-conflict",
                "The confirmation could not be committed. Reload the alert and retry safely.");
        }
    }

    private static void EnsureReviewable(Alert alert)
    {
        var fields = alert.FieldConfirmations
            .Where(field => field.AlertVersion == alert.DraftVersion)
            .ToArray();
        if (alert.State != AlertState.PendingConfirmation
            || alert.ApprovedMessage is null
            || alert.ApprovedMessage.Ciphertext.Length == 0
            || alert.CurrentRecipients.Count == 0
            || fields.Any(field => field.Status != FieldConfirmationStatus.Confirmed))
        {
            throw NotReady();
        }
    }

    private static string RequireIdempotencyKey(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new AlertConfirmationValidationException(
                "idempotency-key-required",
                "An Idempotency-Key header is required for confirmation.");
        }

        if (value.Length > 128 || value.Any(character => character < 0x21 || character > 0x7E))
        {
            throw new AlertConfirmationValidationException(
                "idempotency-key-invalid",
                "The Idempotency-Key header must contain 1 to 128 visible ASCII characters.");
        }

        return value;
    }

    private static string ComputeRequestHash(OrganizationId organizationId, AlertId alertId, int expectedVersion)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"confirm-review|{organizationId.Value:D}|{alertId.Value:D}|{expectedVersion}")));

    private static string EncodeResult(ConfirmAlertReviewResult result)
        => $"{result.AlertId:D}|{result.ConfirmedVersion}|{result.State}";

    private static ConfirmAlertReviewResult DecodeResult(string value, bool replayed)
    {
        var parts = value.Split('|', StringSplitOptions.None);
        if (parts.Length != 3
            || !Guid.TryParse(parts[0], out var alertId)
            || !int.TryParse(parts[1], out var version)
            || !string.Equals(parts[2], AlertState.DispatchQueued.ToString(), StringComparison.Ordinal))
        {
            throw new AlertReviewValidationException(
                "confirmation-conflict",
                "The stored confirmation result is invalid. Reload the alert and retry safely.");
        }

        return new ConfirmAlertReviewResult(alertId, version, parts[2], replayed);
    }

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };

    private static AlertReviewValidationException NotReady()
        => new(
            "review-not-ready",
            "The alert changed or is not complete for exact review. Reload the alert and confirm the current version.");
}
