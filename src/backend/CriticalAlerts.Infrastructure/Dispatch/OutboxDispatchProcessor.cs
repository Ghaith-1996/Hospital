using System.Text.Json;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Observability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class OutboxDispatchProcessor(
    CriticalAlertsDbContext db,
    IEnumerable<INotificationChannel> channels,
    INotificationStatusNormalizer statusNormalizer,
    ISimulationDispatchScenarioStore scenarioStore,
    TimeProvider time,
    IOptions<DispatchWorkerOptions> options,
    ILogger<OutboxDispatchProcessor> logger,
    ILoggerFactory? loggerFactory = null) : IOutboxDispatchProcessor
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationChannel> channelsByType =
        channels.ToDictionary(channel => channel.ChannelType);

    /// <summary>Synthetic reference for attempts that only await a delivery report; never resolves to a recipient.</summary>
    internal const string AwaitingReportEndpointReference = "SIM-AWAITING-REPORT";

    public async Task<DispatchProcessingResult> ProcessNextAsync(
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new ArgumentException("A dispatch worker lease owner is required.", nameof(leaseOwner));
        }

        var workerOptions = options.Value;
        workerOptions.Validate();
        var now = RequireUtc(time.GetUtcNow(), "worker clock");
        var message = await ClaimNextAsync(leaseOwner, now, workerOptions, cancellationToken);
        if (message is null)
        {
            return new DispatchProcessingResult(false, false, false, null, "no-work");
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await AlertMutationLock.AcquireAsync(db, message.OrganizationId, new AlertId(message.AggregateId), cancellationToken);
            _ = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM outbox_messages WHERE id = {message.Id.Value} FOR UPDATE")
                .SingleAsync(cancellationToken);
            await db.Entry(message).ReloadAsync(cancellationToken);
            if (message.LeaseOwner != leaseOwner.Trim() || message.ProcessingState != OutboxProcessingState.Processing)
                return new DispatchProcessingResult(false, false, false, message.Id.Value, "lease-lost");
            var result = await ProcessMessageAsync(message, leaseOwner.Trim(), now, workerOptions, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DispatchValidationException)
        {
            return await PersistWorkerFailureAsync(message, leaseOwner.Trim(), now, workerOptions,
                "dispatch-validation", permanent: true, cancellationToken);
        }
        catch (DomainException)
        {
            return await PersistWorkerFailureAsync(message, leaseOwner.Trim(), now, workerOptions,
                "domain-validation", permanent: true, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            CriticalAlertsOperationalLog.WorkerState(
                loggerFactory?.CreateLogger(CriticalAlertsOperationalLog.Category) ?? logger, "dispatch", true);
            return await PersistWorkerFailureAsync(message, leaseOwner.Trim(), now, workerOptions,
                "worker-error", permanent: false, cancellationToken);
        }
    }

    private async Task<OutboxMessage?> ClaimNextAsync(
        string leaseOwner,
        DateTimeOffset now,
        DispatchWorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var messageId = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value"
                FROM outbox_messages
                WHERE (processing_state = 'Pending' AND next_attempt_at_utc <= {now})
                   OR (processing_state = 'Processing'
                       AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= {now}))
                ORDER BY created_at_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (messageId == Guid.Empty)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var message = await db.OutboxMessages
            .SingleAsync(item => item.Id == new OutboxMessageId(messageId), cancellationToken);
        if (!message.TryAcquireLease(leaseOwner, now, now.Add(workerOptions.LeaseDuration)))
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return message;
    }

    private async Task<DispatchProcessingResult> ProcessMessageAsync(
        OutboxMessage message,
        string leaseOwner,
        DateTimeOffset now,
        DispatchWorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        var payload = ParsePayload(message);
        var alert = await db.Alerts
            .Include(item => item.FieldConfirmations)
            .Include(item => item.RecipientSelections)
            .Include(item => item.StateTransitions)
            .SingleOrDefaultAsync(
                item => item.OrganizationId == message.OrganizationId && item.Id == payload.AlertId,
                cancellationToken);
        if (payload.RecipientSelectionIds is not null && alert is not null)
        {
            await db.Entry(alert).ReloadAsync(cancellationToken);
            if (alert.ConfirmedDraftVersion?.Value == payload.DraftVersion && alert.DraftVersion.Value == payload.DraftVersion
                && alert.State is AlertState.Resolved or AlertState.Cancelled)
            {
                message.MarkProcessed(leaseOwner, now);
                await db.SaveChangesAsync(cancellationToken);
                return new DispatchProcessingResult(true, false, false, message.Id.Value, "stopped-by-lifecycle");
            }
        }
        if (alert is null
            || alert.Id.Value != message.AggregateId
            || alert.DraftVersion.Value != payload.DraftVersion
            || alert.ConfirmedDraftVersion?.Value != payload.DraftVersion
            || alert.State is not (AlertState.DispatchQueued or AlertState.Active)
            || alert.ApprovedMessage is null
            || alert.ApprovedMessage.Ciphertext.Length == 0)
        {
            throw new DispatchValidationException("alert-not-dispatchable", "The alert is not available for this dispatch version.");
        }

        var recipients = alert.CurrentRecipients
            .Where(item => payload.RecipientSelectionIds is null
                ? item.SelectionSource != RecipientSelectionSource.EscalationPolicy
                : payload.RecipientSelectionIds.Contains(item.Id.Value) && item.SelectionSource == RecipientSelectionSource.EscalationPolicy)
            .OrderBy(item => item.Id.Value)
            .ToArray();
        if (recipients.Length == 0 || (payload.RecipientSelectionIds is not null && recipients.Length != payload.RecipientSelectionIds.Length))
        {
            throw new DispatchValidationException("recipients-missing", "The confirmed dispatch has no recipients.");
        }

        if (payload.RecipientSelectionIds is not null && await db.ResponsibilityAssignments.AnyAsync(row =>
                row.OrganizationId == message.OrganizationId && row.AlertId == alert.Id
                && row.AlertVersion == alert.DraftVersion && row.ReleasedAtUtc == null, cancellationToken))
        {
            message.MarkProcessed(leaseOwner, now);
            await db.SaveChangesAsync(cancellationToken);
            return new DispatchProcessingResult(true, false, false, message.Id.Value, "stopped-by-responsibility");
        }

        var policy = await LoadPolicyAsync(alert, message.OrganizationId, cancellationToken);
        var allowedChannels = ParseAllowedChannels(policy.AllowedChannels);
        var practitionerIds = recipients.Select(item => item.PractitionerId).Distinct().ToArray();
        var practitioners = await db.Practitioners
            .AsNoTracking()
            .Where(item => item.OrganizationId == message.OrganizationId && practitionerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var selectedRoleIds = recipients
            .Where(item => item.PractitionerRoleId is not null)
            .Select(item => item.PractitionerRoleId!.Value)
            .Distinct()
            .ToArray();
        var roles = await db.PractitionerRoles
            .AsNoTracking()
            .Where(item => item.OrganizationId == message.OrganizationId
                && (practitionerIds.Contains(item.PractitionerId) || selectedRoleIds.Contains(item.Id)))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var endpoints = await db.ContactEndpoints
            .AsNoTracking()
            .Where(item => item.OrganizationId == message.OrganizationId
                && item.IsActive
                && practitionerIds.Contains(item.PractitionerId))
            .ToArrayAsync(cancellationToken);
        var attempts = await db.DeliveryAttempts
            .Where(item => item.OrganizationId == message.OrganizationId && item.AlertId == alert.Id)
            .OrderBy(item => item.AttemptNumber)
            .ToListAsync(cancellationToken);
        var correlationId = $"dispatch:{message.Id.Value:N}";
        var retryRequested = false;
        var retryAtUtc = now.Add(workerOptions.RetryDelay);
        var maxAttempts = payload.RecipientSelectionIds is not null ? 1 : Math.Max(1, Math.Min(workerOptions.MaxAttempts, policy.RetryLimit + 1));

        foreach (var recipient in recipients)
        {
            var result = await ProcessRecipientAsync(
                message,
                alert,
                policy,
                allowedChannels,
                recipient,
                practitioners,
                roles,
                endpoints,
                attempts,
                correlationId,
                maxAttempts,
                now,
                workerOptions,
                cancellationToken);
            retryRequested |= result.RetryRequested;
            if (result.RetryAtUtc is DateTimeOffset requestedRetryAt)
            {
                retryAtUtc = requestedRetryAt < retryAtUtc ? requestedRetryAt : retryAtUtc;
            }
        }

        var latestAttempts = recipients
            .Select(recipient => FindLatest(attempts, recipient))
            .Where(attempt => attempt is not null)
            .Select(attempt => attempt!)
            .ToArray();
        if (latestAttempts.Length != recipients.Length)
        {
            throw new DispatchValidationException("delivery-attempt-missing", "The dispatch did not create an attempt for every recipient.");
        }

        var hasOutstandingAttempt = latestAttempts.Any(attempt =>
            attempt.Status is DeliveryAttemptStatus.Requested or DeliveryAttemptStatus.Submitted);
        var hasDeliveredAttempt = latestAttempts.Any(attempt => attempt.Status == DeliveryAttemptStatus.Delivered);
        if (hasOutstandingAttempt || retryRequested)
        {
            if (alert.State == AlertState.DispatchQueued
                && latestAttempts.Any(attempt => attempt.Status is DeliveryAttemptStatus.Submitted or DeliveryAttemptStatus.Delivered))
            {
                alert.MarkActive(now, correlationId);
            }

            return await RescheduleAsync(
                message,
                alert,
                leaseOwner,
                now,
                retryAtUtc,
                hasOutstandingAttempt ? "delivery-pending" : "delivery-retry",
                cancellationToken);
        }

        if (hasDeliveredAttempt)
        {
            if (alert.State == AlertState.DispatchQueued)
            {
                alert.MarkActive(now, correlationId);
            }

            message.MarkProcessed(leaseOwner, now);
            AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.completed", "succeeded", correlationId, now, new
            {
                deliveryState = "delivered",
                recipientCount = latestAttempts.Length,
            });
            await db.SaveChangesAsync(cancellationToken);
            return new DispatchProcessingResult(true, false, false, message.Id.Value, "processed");
        }

        // The original dispatch only aggregates its own recipients. A late primary failure (for example an SMS
        // report timeout) must not fail the whole alert, or stop escalation, while approved backup work has been
        // delivered, is still pending, or holds accepted responsibility: the primary failure stays visible on its
        // attempts, this outbox row and the live warnings, and the workflow remains resolvable.
        if (message.EventType != "EscalationDispatchRequested" && alert.State is AlertState.DispatchQueued or AlertState.Active
            && !await HasContinuingBackupWorkAsync(alert, attempts, cancellationToken))
        {
            alert.MarkFailed(now, correlationId);
            await FailOpenEscalationRunAsync(alert, now, cancellationToken);
        }

        message.MarkFailed(leaseOwner, now, "delivery-failed");
        AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.failed", "failed", correlationId, now, new
        {
            deliveryState = "failed",
            recipientCount = latestAttempts.Length,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new DispatchProcessingResult(false, false, true, message.Id.Value, "permanently-failed");
    }

    private async Task<RecipientProcessingResult> ProcessRecipientAsync(
        OutboxMessage message,
        Alert alert,
        NotificationPolicy policy,
        IReadOnlySet<NotificationChannel> allowedChannels,
        AlertRecipientSelection recipient,
        IReadOnlyDictionary<PractitionerId, Practitioner> practitioners,
        IReadOnlyDictionary<PractitionerRoleId, PractitionerRoleAssignment> roles,
        IReadOnlyCollection<ContactEndpoint> endpoints,
        ICollection<DeliveryAttempt> attempts,
        string correlationId,
        int maxAttempts,
        DateTimeOffset now,
        DispatchWorkerOptions workerOptions,
        CancellationToken cancellationToken)
    {
        var latest = FindLatest(attempts, recipient);
        if (latest?.Status == DeliveryAttemptStatus.Delivered)
        {
            return RecipientProcessingResult.None;
        }

        if (latest?.Status == DeliveryAttemptStatus.Failed
            && (!string.Equals(latest.FailureCategory, "provider-unavailable", StringComparison.Ordinal)
                || latest.AttemptNumber >= maxAttempts))
        {
            return RecipientProcessingResult.None;
        }

        if (latest?.Status is DeliveryAttemptStatus.Requested or DeliveryAttemptStatus.Submitted
            && (!channelsByType.TryGetValue(latest.Channel, out var sender)
                || !string.Equals(sender.ProviderName, latest.Provider, StringComparison.Ordinal)))
        {
            // The provider that may have sent this attempt is no longer configured. A different provider never
            // sent it and cannot confirm or resend it, so the outcome is visibly unconfirmed (manual fallback).
            latest.MarkFailed("delivery-unconfirmed", now);
            AddAudit(alert.OrganizationId, latest.Id.Value, "dispatch.failed", "failed", correlationId, now, new
            {
                channel = recipient.Channel.ToString(),
                attempt = latest.AttemptNumber,
                error = "delivery-unconfirmed",
            });
            await db.SaveChangesAsync(cancellationToken);
            return RecipientProcessingResult.None;
        }

        INotificationChannel channel;
        string endpointReference;
        if (latest?.Status == DeliveryAttemptStatus.Submitted)
        {
            // Accepted by its provider: only the delivery-report window remains. Directory changes after acceptance
            // (removed endpoint, inactive practitioner, invalid role) cannot fail, resend or strand it, and no
            // recipient lookup is needed, so the provider receives a fixed non-routable reference.
            channel = channelsByType[latest.Channel];
            endpointReference = AwaitingReportEndpointReference;
        }
        else
        {
            var route = ResolveRoute(alert, allowedChannels, recipient, practitioners, roles, endpoints);
            if (route.FailureCategory is not null)
            {
                return await HandleRecipientFailureAsync(
                    message,
                    alert,
                    recipient,
                    latest,
                    attempts,
                    route.FailureCategory,
                    correlationId,
                    now,
                    cancellationToken);
            }

            channel = route.Channel!;
            endpointReference = route.EndpointReference!;
        }

        // Read the clock at this recipient's send boundary, after the claim, the alert lock and earlier recipients:
        // the pass-start time can already be older than the provider's replay window.
        var sendNow = RequireUtc(time.GetUtcNow(), "worker clock");
        var attempt = latest;
        if (attempt is null
            || attempt.Status == DeliveryAttemptStatus.Failed)
        {
            var attemptNumber = (latest?.AttemptNumber ?? 0) + 1;
            var attemptKey = CreateAttemptIdempotencyKey(alert, recipient, attemptNumber);
            // A rolled-back dispatch transaction leaves no attempt row, but a provider that already sent this key
            // committed it to the send ledger. Never let a different (or default simulated) provider stand in.
            var originalProvider = await db.ProviderSendRecords.AsNoTracking()
                .Where(row => row.OrganizationId == alert.OrganizationId && row.AttemptIdempotencyKey == attemptKey)
                .Select(row => row.Provider)
                .FirstOrDefaultAsync(cancellationToken);
            if (originalProvider is not null && !string.Equals(originalProvider, channel.ProviderName, StringComparison.Ordinal))
                return await FailUnconfirmedRecreatedAttemptAsync(alert, recipient, attemptNumber, attemptKey, originalProvider,
                    attempts, correlationId, sendNow, cancellationToken);

            attempt = DeliveryAttempt.CreateRequested(
                DeliveryAttemptId.New(),
                alert.OrganizationId,
                alert.Id,
                recipient.Id,
                recipient.Channel,
                attemptNumber,
                attemptKey,
                channel.ProviderName,
                sendNow);
            db.DeliveryAttempts.Add(attempt);
            attempts.Add(attempt);
        }

        var scenario = await scenarioStore.GetAsync(alert.OrganizationId, recipient.Channel, cancellationToken);
        // The actual first-send time, committed on its own connection before the network call: it survives a
        // rollback of this transaction after the provider accepted the send, and a recreated attempt reuses it.
        DateTimeOffset? firstSentAtUtc = attempt.Status == DeliveryAttemptStatus.Requested && channel.RequiresDurableFirstSend
            ? await ProviderSendLedger.GetOrRecordFirstSendAsync(
                db, alert.OrganizationId, channel.ProviderName, attempt.IdempotencyKey, sendNow, cancellationToken)
            : null;
        var request = new NotificationDispatchRequest(
            alert.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            recipient.Id,
            recipient.Channel,
            endpointReference,
            $"alert:{alert.Id.Value:N}:v{alert.DraftVersion.Value}",
            WakeUpText(policy, recipient.Channel),
            attempt.IdempotencyKey,
            correlationId,
            attempt.Status,
            attempt.RequestedAtUtc,
            attempt.SubmittedAtUtc,
            firstSentAtUtc);
        var dispatch = await channel.DispatchAsync(request, scenario, cancellationToken);
        // A real provider may not have issued a reference yet (definite rejection or ambiguous outcome).
        if (!string.IsNullOrWhiteSpace(dispatch.ProviderReference))
            attempt.SetProviderReference(dispatch.ProviderReference);
        var events = dispatch.Events ?? [];
        var providerEventIds = events
            .Where(item => !string.IsNullOrWhiteSpace(item.ProviderEventId))
            .Select(item => item.ProviderEventId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var knownEventIds = (await db.DeliveryEvents
                .AsNoTracking()
                .Where(item => item.OrganizationId == alert.OrganizationId && providerEventIds.Contains(item.ProviderEventId))
                .Select(item => item.ProviderEventId)
                .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var providerEvent in events)
        {
            if (!knownEventIds.Add(providerEvent.ProviderEventId))
            {
                continue;
            }

            var normalized = statusNormalizer.Normalize(attempt.Id, providerEvent);
            ApplyStatus(attempt, normalized, dispatch.FailureCategory, dispatch.ProviderReference);
            db.DeliveryEvents.Add(DeliveryEvent.Create(
                DeliveryEventId.New(),
                alert.OrganizationId,
                attempt.Id,
                providerEvent.EventType,
                normalized.ProviderEventId,
                now,
                SanitizeMetadata(providerEvent.SanitizedMetadata),
                normalized.OccurredAtUtc));
            AddAudit(alert.OrganizationId, attempt.Id.Value, "dispatch.delivery-event", "succeeded", correlationId, now, new
            {
                channel = recipient.Channel.ToString(),
                attempt = attempt.AttemptNumber,
                provider = channel.ProviderName,
                status = normalized.Status.ToString(),
            });
        }

        // A retryable result without events is an ambiguous provider outcome: the attempt stays Requested so
        // the next pass reuses the same idempotency key. The adapter bounds that window and then fails visibly.
        if (events.Count == 0 && attempt.Status == DeliveryAttemptStatus.Requested && !dispatch.Retryable)
        {
            attempt.MarkFailed("provider-no-result", now);
        }

        await db.SaveChangesAsync(cancellationToken);
        var retryRequested = dispatch.Retryable
            && attempt.Status is DeliveryAttemptStatus.Requested or DeliveryAttemptStatus.Submitted or DeliveryAttemptStatus.Failed
            && attempt.AttemptNumber < maxAttempts;
        var retryAt = dispatch.RetryAtUtc is DateTimeOffset providerRetryAt
            ? ClampRetryAt(providerRetryAt, now, workerOptions.RetryDelay)
            : now.Add(workerOptions.RetryDelay);
        return new RecipientProcessingResult(retryRequested, retryRequested ? retryAt : null);
    }

    /// <summary>
    /// Records a recreated attempt that another provider already sent as visibly unconfirmed, keeping that
    /// provider's provenance. It is never dispatched, so no other provider can claim its delivery.
    /// </summary>
    private async Task<RecipientProcessingResult> FailUnconfirmedRecreatedAttemptAsync(
        Alert alert,
        AlertRecipientSelection recipient,
        int attemptNumber,
        string attemptKey,
        string originalProvider,
        ICollection<DeliveryAttempt> attempts,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var attempt = DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            alert.OrganizationId,
            alert.Id,
            recipient.Id,
            recipient.Channel,
            attemptNumber,
            attemptKey,
            originalProvider,
            now);
        attempt.MarkFailed("delivery-unconfirmed", now);
        db.DeliveryAttempts.Add(attempt);
        attempts.Add(attempt);
        AddAudit(alert.OrganizationId, attempt.Id.Value, "dispatch.failed", "failed", correlationId, now, new
        {
            channel = recipient.Channel.ToString(),
            attempt = attempt.AttemptNumber,
            error = "delivery-unconfirmed",
        });
        await db.SaveChangesAsync(cancellationToken);
        return RecipientProcessingResult.None;
    }

    /// <summary>Directory and policy checks for a new or not-yet-accepted attempt, in their original order.</summary>
    private RecipientRoute ResolveRoute(
        Alert alert,
        IReadOnlySet<NotificationChannel> allowedChannels,
        AlertRecipientSelection recipient,
        IReadOnlyDictionary<PractitionerId, Practitioner> practitioners,
        IReadOnlyDictionary<PractitionerRoleId, PractitionerRoleAssignment> roles,
        IReadOnlyCollection<ContactEndpoint> endpoints)
    {
        if (!practitioners.TryGetValue(recipient.PractitionerId, out var practitioner))
            return RecipientRoute.Failure("practitioner-missing");
        if (!practitioner.IsActive)
            return RecipientRoute.Failure("practitioner-inactive");
        if (recipient.PractitionerRoleId is PractitionerRoleId selectedRole
            && (!roles.TryGetValue(selectedRole, out var role)
                || role.PractitionerId != practitioner.Id
                || role.OrganizationId != alert.OrganizationId))
            return RecipientRoute.Failure("role-invalid");
        if (!allowedChannels.Contains(recipient.Channel))
            return RecipientRoute.Failure("channel-not-allowed");
        if (!channelsByType.TryGetValue(recipient.Channel, out var channel))
            return RecipientRoute.Failure("channel-unavailable");

        var endpointKind = ToEndpointKind(recipient.Channel);
        var endpoint = endpoints
            .Where(item => item.PractitionerId == practitioner.Id && item.Kind == endpointKind)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.Id.Value)
            .FirstOrDefault();
        if (endpoint is null || !IsSafeSimulationReference(endpoint.SimulationLabel, "SIM-"))
            return RecipientRoute.Failure("endpoint-unavailable");

        return new RecipientRoute(channel, endpoint.SimulationLabel, null);
    }

    private async Task<RecipientProcessingResult> HandleRecipientFailureAsync(
        OutboxMessage message,
        Alert alert,
        AlertRecipientSelection recipient,
        DeliveryAttempt? latest,
        ICollection<DeliveryAttempt> attempts,
        string category,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Submitted attempts never reach this method: they only await their report and skip routing checks.
        if (latest?.Status == DeliveryAttemptStatus.Requested)
        {
            latest.MarkFailed(category, now);
            AddAudit(alert.OrganizationId, latest.Id.Value, "dispatch.failed", "failed", correlationId, now, new
            {
                channel = recipient.Channel.ToString(),
                attempt = latest.AttemptNumber,
                error = category,
            });
            await db.SaveChangesAsync(cancellationToken);
            return RecipientProcessingResult.None;
        }

        if (latest is not null)
        {
            return RecipientProcessingResult.None;
        }

        var provider = channelsByType.GetValueOrDefault(recipient.Channel)?.ProviderName
            ?? $"simulation-{recipient.Channel.ToString().ToLowerInvariant()}";
        var attempt = DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            alert.OrganizationId,
            alert.Id,
            recipient.Id,
            recipient.Channel,
            1,
            CreateAttemptIdempotencyKey(alert, recipient, 1),
            provider,
            now);
        attempt.MarkFailed(category, now);
        db.DeliveryAttempts.Add(attempt);
        attempts.Add(attempt);
        AddAudit(alert.OrganizationId, attempt.Id.Value, "dispatch.failed", "failed", correlationId, now, new
        {
            channel = recipient.Channel.ToString(),
            attempt = attempt.AttemptNumber,
            error = category,
        });
        await db.SaveChangesAsync(cancellationToken);
        return RecipientProcessingResult.None;
    }

    private async Task<DispatchProcessingResult> RescheduleAsync(
        OutboxMessage message,
        Alert alert,
        string leaseOwner,
        DateTimeOffset now,
        DateTimeOffset retryAtUtc,
        string category,
        CancellationToken cancellationToken)
    {
        var next = retryAtUtc < now ? now : retryAtUtc;
        // Waiting for a provider delivery report re-polls every retry delay; audit only the start of a wait,
        // so a pending report does not create one retry record (and retry metric) per poll.
        var continuingWait = category == "delivery-pending"
            && string.Equals(message.LastErrorCategory, category, StringComparison.Ordinal);
        message.ScheduleRetry(leaseOwner, now, next, category);
        if (!continuingWait) AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.retry-scheduled", "succeeded", $"dispatch:{message.Id.Value:N}", now, new
        {
            nextAttemptAtUtc = next,
            reason = category,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new DispatchProcessingResult(false, true, false, message.Id.Value, "rescheduled");
    }

    private async Task<DispatchProcessingResult> PersistWorkerFailureAsync(
        OutboxMessage claimedMessage,
        string leaseOwner,
        DateTimeOffset now,
        DispatchWorkerOptions workerOptions,
        string category,
        bool permanent,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var alertId = new AlertId(claimedMessage.AggregateId);
        await AlertMutationLock.AcquireAsync(db, claimedMessage.OrganizationId, alertId, cancellationToken);
        var message = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM outbox_messages WHERE id = {claimedMessage.Id.Value} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (message.LeaseOwner != leaseOwner || message.ProcessingState != OutboxProcessingState.Processing)
        {
            return new DispatchProcessingResult(false, false, false, message.Id.Value, "lease-lost");
        }

        var alert = await db.Alerts.SingleOrDefaultAsync(item => item.OrganizationId == message.OrganizationId
            && item.Id == alertId, cancellationToken);
        // Count only real worker failures: routine re-polls while awaiting a delivery report also reclaim the row.
        message.RecordWorkerFailure(leaseOwner);
        var shouldFail = permanent || message.WorkerFailureCount >= workerOptions.MaxAttempts;
        if (shouldFail)
        {
            if (message.EventType != "EscalationDispatchRequested" && alert is not null)
            {
                if (alert.State is AlertState.DispatchQueued or AlertState.Active)
                    alert.MarkFailed(now, $"dispatch:{message.Id.Value:N}");
                if (alert.State == AlertState.Failed)
                    await FailOpenEscalationRunAsync(alert, now, cancellationToken);
            }
            message.MarkFailed(leaseOwner, now, category);
            AddAudit(message.OrganizationId, alert?.Id.Value ?? message.AggregateId, "dispatch.failed", "failed",
                $"dispatch:{message.Id.Value:N}", now, new { error = category });
        }
        else
        {
            message.ScheduleRetry(leaseOwner, now, now.Add(workerOptions.RetryDelay), category);
            if (alert is not null)
            {
                AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.retry-scheduled", "succeeded",
                    $"dispatch:{message.Id.Value:N}", now, new { reason = category });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return shouldFail
            ? new DispatchProcessingResult(false, false, true, message.Id.Value, "permanently-failed")
            : new DispatchProcessingResult(false, true, false, message.Id.Value, "rescheduled");
    }

    /// <summary>
    /// Approved escalation work for the confirmed version that keeps the alert workable: a backup attempt that was
    /// delivered or is still in flight, a queued backup dispatch, or an accepted, unreleased responsibility.
    /// </summary>
    private async Task<bool> HasContinuingBackupWorkAsync(
        Alert alert,
        IEnumerable<DeliveryAttempt> attempts,
        CancellationToken cancellationToken)
    {
        var backupSelections = alert.CurrentRecipients
            .Where(item => item.SelectionSource == RecipientSelectionSource.EscalationPolicy)
            .Select(item => item.Id)
            .ToHashSet();
        if (attempts.Any(item => backupSelections.Contains(item.RecipientSelectionId)
                && item.Status is DeliveryAttemptStatus.Delivered or DeliveryAttemptStatus.Requested or DeliveryAttemptStatus.Submitted))
            return true;

        var aggregateId = alert.Id.Value;
        if (await db.OutboxMessages.AsNoTracking().AnyAsync(item => item.OrganizationId == alert.OrganizationId
                && item.AggregateId == aggregateId && item.EventType == "EscalationDispatchRequested"
                && (item.ProcessingState == OutboxProcessingState.Pending || item.ProcessingState == OutboxProcessingState.Processing),
                cancellationToken))
            return true;

        return await db.ResponsibilityAssignments.AsNoTracking().AnyAsync(item => item.OrganizationId == alert.OrganizationId
            && item.AlertId == alert.Id && item.AlertVersion == alert.DraftVersion && item.ReleasedAtUtc == null, cancellationToken);
    }

    private async Task FailOpenEscalationRunAsync(Alert alert, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (alert.ConfirmedDraftVersion is not AlertDraftVersion version) return;
        var run = await db.EscalationRuns.FromSqlInterpolated($"""
            SELECT * FROM escalation_runs
            WHERE organization_id = {alert.OrganizationId.Value} AND alert_id = {alert.Id.Value}
                AND alert_version = {version.Value} AND state IN ('Scheduled', 'Running', 'Paused', 'Exhausted')
            FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (run is null) return;
        run.Stop(EscalationEventKind.ProcessingFailed, now);
        db.EscalationEvents.Add(run.Record(EscalationEventKind.ProcessingFailed, now));
        AddAudit(alert.OrganizationId, alert.Id.Value, "escalation-processing-failed", "ProcessingFailed",
            $"escalation:{run.Id.Value:N}", now, new { stepSequence = run.CurrentStep, simulationOnly = true });
    }

    private async Task<NotificationPolicy> LoadPolicyAsync(
        Alert alert,
        OrganizationId organizationId,
        CancellationToken cancellationToken)
    {
        var query = db.NotificationPolicies
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.IsActive);
        if (!string.Equals(alert.DemoNotificationPolicyVersion, "DEMO", StringComparison.Ordinal))
        {
            query = query.Where(item => item.Version == alert.DemoNotificationPolicyVersion);
        }

        var policy = await query
            .OrderBy(item => item.Version)
            .FirstOrDefaultAsync(cancellationToken);
        return policy ?? throw new DispatchValidationException("notification-policy-missing", "The dispatch notification policy is unavailable.");
    }

    private static ParsedDispatchPayload ParsePayload(OutboxMessage message)
    {
        try
        {
            using var document = JsonDocument.Parse(message.PayloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DispatchValidationException("payload-invalid", "The dispatch payload must contain identifiers only.");
            }

            Guid? alertId = null;
            int? draftVersion = null;
            Guid[]? recipientSelectionIds = null;
            var escalation = message.EventType == "EscalationDispatchRequested";
            if (!escalation && message.EventType != "AlertDispatchRequested")
                throw new DispatchValidationException("payload-invalid", "Unsupported dispatch event.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new DispatchValidationException("payload-invalid", "The dispatch payload contains duplicate fields.");
                }

                switch (property.Name)
                {
                    case "alertId" when property.Value.ValueKind == JsonValueKind.String
                        && property.Value.TryGetGuid(out var parsedAlertId):
                        alertId = parsedAlertId;
                        break;
                    case "draftVersion" when property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt32(out var parsedVersion):
                        draftVersion = parsedVersion;
                        break;
                    case "recipientSelectionIds" when escalation && property.Value.ValueKind == JsonValueKind.Array:
                        var ids = new List<Guid>();
                        foreach (var value in property.Value.EnumerateArray())
                        {
                            if (value.ValueKind != JsonValueKind.String || !value.TryGetGuid(out var id) || id == Guid.Empty || ids.Contains(id))
                                throw new DispatchValidationException("payload-invalid", "Unique recipient selection identifiers are required.");
                            ids.Add(id);
                        }
                        recipientSelectionIds = ids.ToArray();
                        break;
                    default:
                        throw new DispatchValidationException("payload-invalid", "The dispatch payload contains an unsupported field.");
                }
            }

            if (names.Count != (escalation ? 3 : 2) || alertId is null || draftVersion is null || draftVersion <= 0
                || (escalation && (recipientSelectionIds is null || recipientSelectionIds.Length == 0)))
            {
                throw new DispatchValidationException("payload-invalid", "The dispatch payload requires an alert ID and draft version.");
            }

            if (alertId.Value != message.AggregateId)
            {
                throw new DispatchValidationException("payload-mismatch", "The dispatch payload does not match its outbox aggregate.");
            }

            return new ParsedDispatchPayload(new AlertId(alertId.Value), draftVersion.Value, recipientSelectionIds);
        }
        catch (JsonException)
        {
            throw new DispatchValidationException("payload-invalid", "The dispatch payload is not valid JSON.");
        }
    }

    private void ApplyStatus(
        DeliveryAttempt attempt,
        NormalizedDeliveryEvent normalized,
        string? fallbackFailureCategory,
        string providerReference)
    {
        if (!string.IsNullOrWhiteSpace(providerReference))
            attempt.SetProviderReference(providerReference);
        switch (normalized.Status)
        {
            case DeliveryAttemptStatus.Submitted when !string.IsNullOrWhiteSpace(providerReference):
                attempt.MarkSubmitted(providerReference, normalized.OccurredAtUtc);
                break;
            case DeliveryAttemptStatus.Delivered:
                attempt.MarkDelivered(normalized.OccurredAtUtc);
                break;
            case DeliveryAttemptStatus.Failed:
                attempt.MarkFailed(
                    SafeFailureCategory(normalized.FailureCategory ?? fallbackFailureCategory ?? "provider-failed"),
                    normalized.OccurredAtUtc);
                break;
            default:
                throw new DispatchValidationException("delivery-status-invalid", "The provider event status is not supported.");
        }
    }

    private void AddAudit(
        OrganizationId organizationId,
        Guid resourceId,
        string action,
        string outcome,
        string correlationId,
        DateTimeOffset now,
        object metadata)
    {
        db.AuditEvents.Add(AuditEvent.Record(
            AuditEventId.New(),
            organizationId,
            "worker",
            null,
            action,
            action.StartsWith("dispatch.delivery", StringComparison.Ordinal) ? "delivery-attempt" : "alert",
            resourceId,
            outcome,
            correlationId,
            JsonSerializer.Serialize(metadata),
            now));
    }

    private static DeliveryAttempt? FindLatest(
        IEnumerable<DeliveryAttempt> attempts,
        AlertRecipientSelection recipient)
        => attempts
            .Where(item => item.OrganizationId == recipient.OrganizationId
                && item.AlertId == recipient.AlertId
                && item.RecipientSelectionId == recipient.Id
                && item.Channel == recipient.Channel)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.RequestedAtUtc)
            .FirstOrDefault();

    private static IReadOnlySet<NotificationChannel> ParseAllowedChannels(string allowedChannels)
    {
        var channels = new HashSet<NotificationChannel>();
        foreach (var value in allowedChannels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<NotificationChannel>(value, ignoreCase: false, out var channel)
                || !Enum.IsDefined(channel))
            {
                throw new DispatchValidationException("notification-policy-invalid", "The dispatch notification policy contains an unsupported channel.");
            }

            channels.Add(channel);
        }

        if (channels.Count == 0)
        {
            throw new DispatchValidationException("notification-policy-invalid", "The dispatch notification policy contains no allowed channels.");
        }

        return channels;
    }

    private static string WakeUpText(NotificationPolicy policy, NotificationChannel channel)
    {
        var text = channel switch
        {
            NotificationChannel.Sms => policy.GenericSmsTemplate,
            NotificationChannel.Voice => policy.GenericVoiceTemplate,
            _ => "SIMULATION: secure message available.",
        };
        if (!text.StartsWith("SIMULATION:", StringComparison.Ordinal) || text.Length > 240)
        {
            throw new DispatchValidationException("wake-up-text-invalid", "Dispatch wake-up text must be generic synthetic content.");
        }

        return text;
    }

    private static ContactEndpointKind ToEndpointKind(NotificationChannel channel)
        => channel switch
        {
            NotificationChannel.SecureMessage => ContactEndpointKind.SecureMessage,
            NotificationChannel.Sms => ContactEndpointKind.Sms,
            NotificationChannel.Voice => ContactEndpointKind.Voice,
            _ => throw new DispatchValidationException("channel-invalid", "The dispatch channel is not supported."),
        };

    private static string CreateAttemptIdempotencyKey(
        Alert alert,
        AlertRecipientSelection recipient,
        int attemptNumber)
        => $"alert-dispatch:{alert.Id.Value:N}:v{alert.DraftVersion.Value}:r{recipient.Id.Value:N}:c{(int)recipient.Channel}:a{attemptNumber}";

    private static string SanitizeMetadata(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500 || value.Any(char.IsControl))
        {
            throw new DispatchValidationException("provider-metadata-invalid", "Provider metadata is not safe for persistence.");
        }

        return value.Trim();
    }

    private static string SafeFailureCategory(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length is 0 or > 64
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            return "provider-failed";
        }

        return normalized;
    }

    private static bool IsSafeSimulationReference(string value, string prefix)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 100
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_');

    private static DateTimeOffset ClampRetryAt(DateTimeOffset requested, DateTimeOffset now, TimeSpan retryDelay)
    {
        var utc = RequireUtc(requested, "retry time");
        return utc < now ? now : utc > now.Add(retryDelay) ? now.Add(retryDelay) : utc;
    }

    private static DateTimeOffset RequireUtc(DateTimeOffset value, string name)
        => value.Offset == TimeSpan.Zero
            ? value
            : throw new DispatchValidationException("clock-not-utc", $"The {name} must be UTC.");

    private sealed record ParsedDispatchPayload(AlertId AlertId, int DraftVersion, Guid[]? RecipientSelectionIds);

    private sealed record RecipientRoute(INotificationChannel? Channel, string? EndpointReference, string? FailureCategory)
    {
        public static RecipientRoute Failure(string category) => new(null, null, category);
    }

    private sealed record RecipientProcessingResult(bool RetryRequested, DateTimeOffset? RetryAtUtc)
    {
        public static RecipientProcessingResult None { get; } = new(false, null);
    }
}
