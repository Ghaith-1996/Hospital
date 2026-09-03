using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CriticalAlerts.Application.Responses;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CriticalAlerts.Infrastructure.Responses;

public sealed class RecipientResponseService(
    CriticalAlertsDbContext db,
    PractitionerIdentityResolver identities,
    TimeProvider time) : IRecipientResponseService
{
    public async Task<OpenedRecipientAlertResult?> MarkOpenedAsync(
        OrganizationId organizationId,
        UserId userId,
        string correlationId,
        AlertId alertId,
        OpenRecipientAlertRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = RequireIdempotencyKey(idempotencyKey);
        var requestHash = Hash($"recipient-open|{organizationId.Value:D}|{userId.Value:D}|{alertId.Value:D}|{request.ExpectedVersion}");
        var now = RequireUtc(time.GetUtcNow());
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replay = await FindIdempotencyAsync(organizationId, "recipient-open", key, requestHash, cancellationToken);
            if (replay is not null)
            {
                return await BuildOpenedResultAsync(organizationId, userId, alertId, replayed: true, cancellationToken);
            }

            var context = await LoadAddressedAlertAsync(organizationId, userId, alertId, cancellationToken);
            if (context is null)
            {
                return null;
            }

            RequireExactVersion(context.Alert, request.ExpectedVersion);
            var selectionIds = context.Selections
                .Where(selection => selection.Channel == NotificationChannel.SecureMessage)
                .Select(selection => selection.Id)
                .ToArray();
            if (selectionIds.Length == 0)
            {
                throw new RecipientResponseValidationException(
                    "opened-not-supported",
                    "Opened observations are only available for SecureMessage selections.");
            }

            var attempts = await db.DeliveryAttempts
                .Where(attempt => attempt.OrganizationId == organizationId
                    && attempt.AlertId == alertId
                    && attempt.Channel == NotificationChannel.SecureMessage
                    && selectionIds.Contains(attempt.RecipientSelectionId))
                .ToArrayAsync(cancellationToken);
            if (attempts.Length == 0)
            {
                throw new RecipientResponseValidationException(
                    "opened-not-supported",
                    "A SecureMessage delivery attempt is required before recording an opened observation.");
            }

            foreach (var attempt in attempts)
            {
                attempt.MarkOpened(now);
            }

            var idempotency = IdempotencyRecord.Start(
                IdempotencyRecordId.New(),
                organizationId,
                "recipient-open",
                key,
                requestHash,
                now);
            idempotency.Complete($"open:{alertId.Value:N}:{request.ExpectedVersion}");
            db.IdempotencyRecords.Add(idempotency);
            db.AuditEvents.Add(AuditEvent.Record(
                AuditEventId.New(),
                organizationId,
                "user",
                userId,
                "recipient.opened",
                "alert",
                alertId.Value,
                "succeeded",
                correlationId,
                JsonSerializer.Serialize(new
                {
                    simulationOnly = true,
                    alertVersion = request.ExpectedVersion,
                    secureMessageAttemptCount = attempts.Length,
                }),
                now));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OpenedRecipientAlertResult(
                alertId.Value,
                request.ExpectedVersion,
                attempts.Select(attempt => attempt.OpenedAtUtc).Where(value => value is not null).Min(),
                Replayed: false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var replay = await FindIdempotencyAsync(organizationId, "recipient-open", key, requestHash, cancellationToken);
            if (replay is not null)
            {
                return await BuildOpenedResultAsync(organizationId, userId, alertId, replayed: true, cancellationToken);
            }

            throw Conflict("response-conflict", "The opened state changed concurrently. Reload the alert.");
        }
    }

    public async Task<RecipientResponseResult?> RecordAsync(
        OrganizationId organizationId,
        UserId userId,
        string correlationId,
        AlertId alertId,
        RecordRecipientResponseRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var responseType = ParseResponseType(request.ResponseType);
        var key = RequireIdempotencyKey(idempotencyKey);
        var requestHash = Hash($"recipient-response|{organizationId.Value:D}|{userId.Value:D}|{alertId.Value:D}|{request.ExpectedVersion}|{responseType}");
        var now = RequireUtc(time.GetUtcNow());
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var replayReference = await FindIdempotencyAsync(
                organizationId,
                "recipient-response",
                key,
                requestHash,
                cancellationToken);
            if (replayReference is not null)
            {
                return await BuildResponseResultAsync(
                    organizationId,
                    userId,
                    alertId,
                    request.ExpectedVersion,
                    responseType,
                    replayed: true,
                    cancellationToken);
            }

            var context = await LoadAddressedAlertAsync(organizationId, userId, alertId, cancellationToken);
            if (context is null)
            {
                return null;
            }

            RequireExactVersion(context.Alert, request.ExpectedVersion);
            var category = responseType == RecipientResponseType.Acknowledged
                ? RecipientResponseCategory.Acknowledgement
                : RecipientResponseCategory.TerminalDisposition;
            var existing = await db.RecipientResponses.SingleOrDefaultAsync(response =>
                    response.OrganizationId == organizationId
                    && response.AlertId == alertId
                    && response.AlertVersion == context.Alert.ConfirmedDraftVersion!.Value
                    && response.PractitionerId == context.PractitionerId
                    && response.Category == category,
                cancellationToken);
            if (existing is not null && existing.ResponseType != responseType)
            {
                throw Conflict(
                    "terminal-disposition-conflict",
                    "A different terminal disposition is already recorded for this practitioner and alert version.");
            }

            var idempotency = IdempotencyRecord.Start(
                IdempotencyRecordId.New(),
                organizationId,
                "recipient-response",
                key,
                requestHash,
                now);
            if (existing is not null)
            {
                idempotency.Complete(existing.Id.Value.ToString("D"));
                db.IdempotencyRecords.Add(idempotency);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return await BuildResponseResultAsync(
                    organizationId,
                    userId,
                    alertId,
                    request.ExpectedVersion,
                    responseType,
                    replayed: true,
                    cancellationToken);
            }

            var response = RecipientResponse.Record(
                RecipientResponseId.New(),
                organizationId,
                alertId,
                context.Alert.ConfirmedDraftVersion!.Value,
                context.PractitionerId,
                responseType,
                userId,
                now,
                ReasonCode(responseType));
            db.RecipientResponses.Add(response);
            var assignment = ResponsibilityAssignment.FromResponse(response);
            if (assignment is not null)
            {
                db.ResponsibilityAssignments.Add(assignment);
            }

            idempotency.Complete(response.Id.Value.ToString("D"));
            db.IdempotencyRecords.Add(idempotency);
            db.AuditEvents.Add(AuditEvent.Record(
                AuditEventId.New(),
                organizationId,
                "user",
                userId,
                $"recipient.response.{responseType.ToString().ToLowerInvariant()}",
                "alert",
                alertId.Value,
                "succeeded",
                correlationId,
                JsonSerializer.Serialize(new
                {
                    simulationOnly = true,
                    alertVersion = request.ExpectedVersion,
                    practitionerId = context.PractitionerId.Value,
                    responseType = responseType.ToString(),
                }),
                now));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await BuildResponseResultAsync(
                organizationId,
                userId,
                alertId,
                request.ExpectedVersion,
                responseType,
                replayed: false,
                cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var replayReference = await FindIdempotencyAsync(
                organizationId,
                "recipient-response",
                key,
                requestHash,
                cancellationToken);
            if (replayReference is not null)
            {
                return await BuildResponseResultAsync(
                    organizationId,
                    userId,
                    alertId,
                    request.ExpectedVersion,
                    responseType,
                    replayed: true,
                    cancellationToken);
            }

            var context = await LoadAddressedAlertAsync(organizationId, userId, alertId, cancellationToken);
            if (context is null)
            {
                return null;
            }

            var existing = await db.RecipientResponses.AsNoTracking().SingleOrDefaultAsync(response =>
                    response.OrganizationId == organizationId
                    && response.AlertId == alertId
                    && response.AlertVersion == context.Alert.ConfirmedDraftVersion!.Value
                    && response.PractitionerId == context.PractitionerId
                    && response.Category == (responseType == RecipientResponseType.Acknowledged
                        ? RecipientResponseCategory.Acknowledgement
                        : RecipientResponseCategory.TerminalDisposition),
                cancellationToken);
            if (existing?.ResponseType == responseType)
            {
                return await BuildResponseResultAsync(
                    organizationId,
                    userId,
                    alertId,
                    request.ExpectedVersion,
                    responseType,
                    replayed: true,
                    cancellationToken);
            }

            throw Conflict(
                existing is null ? "response-conflict" : "terminal-disposition-conflict",
                "The practitioner response changed concurrently. Reload the alert.");
        }
    }

    private async Task<AddressedAlert?> LoadAddressedAlertAsync(
        OrganizationId organizationId,
        UserId userId,
        AlertId alertId,
        CancellationToken cancellationToken)
    {
        var practitionerId = await identities.ResolveAsync(organizationId, userId, cancellationToken)
            ?? throw new RecipientResponseValidationException(
                "practitioner-link-required",
                "The authenticated practitioner identity is not linked to a directory practitioner.");
        var alert = await db.Alerts
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Id == alertId
                && item.State == AlertState.Active
                && item.ConfirmedDraftVersion != null,
                cancellationToken);
        if (alert is null)
        {
            return null;
        }

        var selections = await db.AlertRecipientSelections
            .Where(selection => selection.OrganizationId == organizationId
                && selection.AlertId == alertId
                && selection.AlertVersion == alert.ConfirmedDraftVersion!.Value
                && selection.PractitionerId == practitionerId)
            .ToArrayAsync(cancellationToken);
        return selections.Length == 0 ? null : new AddressedAlert(alert, practitionerId, selections);
    }

    private async Task<string?> FindIdempotencyAsync(
        OrganizationId organizationId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item =>
                item.OrganizationId == organizationId
                && item.OperationType == operation
                && item.IdempotencyKey == key,
            cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (!FixedEquals(record.RequestHash, requestHash))
        {
            throw Conflict("idempotency-conflict", "The idempotency key was already used for a different request.");
        }

        if (record.Status != IdempotencyProcessingStatus.Completed)
        {
            throw Conflict("response-in-progress", "A response with this idempotency key is already in progress.");
        }

        return record.ResultReference;
    }

    private async Task<OpenedRecipientAlertResult?> BuildOpenedResultAsync(
        OrganizationId organizationId,
        UserId userId,
        AlertId alertId,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var context = await LoadAddressedAlertAsync(organizationId, userId, alertId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var selectionIds = context.Selections
            .Where(selection => selection.Channel == NotificationChannel.SecureMessage)
            .Select(selection => selection.Id)
            .ToArray();
        var openedAt = await db.DeliveryAttempts.AsNoTracking()
            .Where(attempt => attempt.OrganizationId == organizationId
                && attempt.AlertId == alertId
                && selectionIds.Contains(attempt.RecipientSelectionId)
                && attempt.OpenedAtUtc != null)
            .Select(attempt => attempt.OpenedAtUtc)
            .MinAsync(cancellationToken);
        return new OpenedRecipientAlertResult(
            alertId.Value,
            context.Alert.ConfirmedDraftVersion!.Value.Value,
            openedAt,
            replayed);
    }

    private async Task<RecipientResponseResult> BuildResponseResultAsync(
        OrganizationId organizationId,
        UserId userId,
        AlertId alertId,
        int version,
        RecipientResponseType responseType,
        bool replayed,
        CancellationToken cancellationToken)
    {
        var practitionerId = await identities.ResolveAsync(organizationId, userId, cancellationToken)
            ?? throw new RecipientResponseValidationException(
                "practitioner-link-required",
                "The authenticated practitioner identity is not linked to a directory practitioner.");
        var responses = await db.RecipientResponses.AsNoTracking()
            .Where(response => response.OrganizationId == organizationId
                && response.AlertId == alertId
                && response.AlertVersion == new AlertDraftVersion(version)
                && response.PractitionerId == practitionerId)
            .ToArrayAsync(cancellationToken);
        var acknowledgement = responses.SingleOrDefault(response => response.IsAcknowledgement);
        var terminal = responses.SingleOrDefault(response => response.IsTerminalDisposition);
        var assignment = await db.ResponsibilityAssignments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.AlertId == alertId
                && item.AlertVersion == new AlertDraftVersion(version)
                && item.PractitionerId == practitionerId,
                cancellationToken);
        return new RecipientResponseResult(
            alertId.Value,
            version,
            responseType.ToString(),
            acknowledgement?.OccurredAtUtc,
            terminal?.ResponseType.ToString(),
            assignment?.AcceptedAtUtc,
            replayed);
    }

    private static void RequireExactVersion(Alert alert, int expectedVersion)
    {
        if (alert.ConfirmedDraftVersion?.Value != expectedVersion)
        {
            throw Conflict("alert-version-stale", "The alert version changed. Reload the alert before responding.");
        }
    }

    private static RecipientResponseType ParseResponseType(string? value)
        => value switch
        {
            nameof(RecipientResponseType.Acknowledged) => RecipientResponseType.Acknowledged,
            nameof(RecipientResponseType.Accepted) => RecipientResponseType.Accepted,
            nameof(RecipientResponseType.Declined) => RecipientResponseType.Declined,
            nameof(RecipientResponseType.Unavailable) => RecipientResponseType.Unavailable,
            _ => throw new RecipientResponseValidationException(
                "response-type-invalid",
                "The Phase 8 simulation response must be Acknowledged, Accepted, Declined, or Unavailable."),
        };

    private static string ReasonCode(RecipientResponseType responseType)
        => responseType switch
        {
            RecipientResponseType.Acknowledged => "simulation-acknowledged",
            RecipientResponseType.Accepted => "simulation-responsibility-accepted",
            RecipientResponseType.Declined => "simulation-declined",
            RecipientResponseType.Unavailable => "simulation-unavailable",
            _ => throw new InvalidOperationException("The response type is not available in Phase 8."),
        };

    private static string RequireIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RecipientResponseValidationException(
                "idempotency-key-required",
                "An Idempotency-Key header is required for recipient actions.");
        }

        var normalized = value.Trim();
        if (normalized.Length > 100 || normalized.Any(character => character < 0x21 || character > 0x7E))
        {
            throw new RecipientResponseValidationException(
                "idempotency-key-invalid",
                "The Idempotency-Key header must contain 1 to 100 visible ASCII characters.");
        }

        return normalized;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new InvalidOperationException("The simulation response clock must be UTC.");

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static bool IsUniqueViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static RecipientResponseValidationException Conflict(string code, string message)
        => new(code, message);

    private sealed record AddressedAlert(
        Alert Alert,
        PractitionerId PractitionerId,
        IReadOnlyCollection<AlertRecipientSelection> Selections);
}
