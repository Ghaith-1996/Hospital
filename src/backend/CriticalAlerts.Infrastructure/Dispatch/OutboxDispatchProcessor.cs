using System.Text.Json;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Domain.Reliability;
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
    ILogger<OutboxDispatchProcessor> logger) : IOutboxDispatchProcessor
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationChannel> channelsByType =
        channels.ToDictionary(channel => channel.ChannelType);

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
            return await ProcessMessageAsync(message, leaseOwner.Trim(), now, workerOptions, cancellationToken);
        }
        catch (DispatchValidationException)
        {
            var alert = await FindAlertForFailureAsync(message, cancellationToken);
            return await PermanentlyFailAsync(message, alert, leaseOwner.Trim(), now, "dispatch-validation", cancellationToken);
        }
        catch (DomainException)
        {
            var alert = await FindAlertForFailureAsync(message, cancellationToken);
            return await PermanentlyFailAsync(message, alert, leaseOwner.Trim(), now, "domain-validation", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning("Simulation dispatch worker encountered a retryable internal failure.");
            var alert = await FindAlertForFailureAsync(message, cancellationToken);
            return await RetryOrFailAsync(message, alert, leaseOwner.Trim(), now, workerOptions, "worker-error", cancellationToken);
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
            .OrderBy(item => item.Id.Value)
            .ToArray();
        if (recipients.Length == 0)
        {
            throw new DispatchValidationException("recipients-missing", "The confirmed dispatch has no recipients.");
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
        var maxAttempts = Math.Max(1, Math.Min(workerOptions.MaxAttempts, policy.RetryLimit + 1));

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

        if (alert.State is AlertState.DispatchQueued or AlertState.Active)
        {
            alert.MarkFailed(now, correlationId);
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

        if (!practitioners.TryGetValue(recipient.PractitionerId, out var practitioner))
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "practitioner-missing",
                correlationId,
                now,
                cancellationToken);
        }

        if (!practitioner.IsActive)
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "practitioner-inactive",
                correlationId,
                now,
                cancellationToken);
        }

        if (recipient.PractitionerRoleId is PractitionerRoleId selectedRole
            && (!roles.TryGetValue(selectedRole, out var role)
                || role.PractitionerId != practitioner.Id
                || role.OrganizationId != alert.OrganizationId))
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "role-invalid",
                correlationId,
                now,
                cancellationToken);
        }

        if (!allowedChannels.Contains(recipient.Channel))
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "channel-not-allowed",
                correlationId,
                now,
                cancellationToken);
        }

        if (!channelsByType.TryGetValue(recipient.Channel, out var channel))
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "channel-unavailable",
                correlationId,
                now,
                cancellationToken);
        }

        var endpointKind = ToEndpointKind(recipient.Channel);
        var endpoint = endpoints
            .Where(item => item.PractitionerId == practitioner.Id && item.Kind == endpointKind)
            .OrderByDescending(item => item.IsPrimary)
            .ThenBy(item => item.Id.Value)
            .FirstOrDefault();
        if (endpoint is null || !IsSafeSimulationReference(endpoint.SimulationLabel, "SIM-"))
        {
            return await HandleRecipientFailureAsync(
                message,
                alert,
                recipient,
                latest,
                attempts,
                "endpoint-unavailable",
                correlationId,
                now,
                cancellationToken);
        }

        var attempt = latest;
        if (attempt is null
            || attempt.Status == DeliveryAttemptStatus.Failed)
        {
            var attemptNumber = (latest?.AttemptNumber ?? 0) + 1;
            attempt = DeliveryAttempt.CreateRequested(
                DeliveryAttemptId.New(),
                alert.OrganizationId,
                alert.Id,
                recipient.Id,
                recipient.Channel,
                attemptNumber,
                CreateAttemptIdempotencyKey(alert, recipient, attemptNumber),
                channel.ProviderName,
                now);
            db.DeliveryAttempts.Add(attempt);
            attempts.Add(attempt);
        }

        var scenario = await scenarioStore.GetAsync(alert.OrganizationId, recipient.Channel, cancellationToken);
        var request = new NotificationDispatchRequest(
            alert.OrganizationId,
            alert.Id,
            alert.DraftVersion,
            recipient.Id,
            recipient.Channel,
            endpoint.SimulationLabel,
            $"alert:{alert.Id.Value:N}:v{alert.DraftVersion.Value}",
            WakeUpText(policy, recipient.Channel),
            attempt.IdempotencyKey,
            correlationId,
            attempt.Status);
        var dispatch = await channel.DispatchAsync(request, scenario, cancellationToken);
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

        if (events.Count == 0 && attempt.Status == DeliveryAttemptStatus.Requested)
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
        if (latest?.Status is DeliveryAttemptStatus.Submitted or DeliveryAttemptStatus.Requested)
        {
            if (latest.Status == DeliveryAttemptStatus.Requested)
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

            return new RecipientProcessingResult(true, now.AddSeconds(1));
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
        message.ScheduleRetry(leaseOwner, now, next, category);
        AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.retry-scheduled", "succeeded", $"dispatch:{message.Id.Value:N}", now, new
        {
            nextAttemptAtUtc = next,
            reason = category,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new DispatchProcessingResult(false, true, false, message.Id.Value, "rescheduled");
    }

    private async Task<DispatchProcessingResult> PermanentlyFailAsync(
        OutboxMessage message,
        Alert? alert,
        string leaseOwner,
        DateTimeOffset now,
        string category,
        CancellationToken cancellationToken)
    {
        if (alert is not null && alert.State is AlertState.DispatchQueued or AlertState.Active)
        {
            alert.MarkFailed(now, $"dispatch:{message.Id.Value:N}");
        }

        message.MarkFailed(leaseOwner, now, category);
        AddAudit(
            message.OrganizationId,
            alert?.Id.Value ?? message.AggregateId,
            "dispatch.failed",
            "failed",
            $"dispatch:{message.Id.Value:N}",
            now,
            new { error = category });
        await db.SaveChangesAsync(cancellationToken);
        return new DispatchProcessingResult(false, false, true, message.Id.Value, "permanently-failed");
    }

    private async Task<DispatchProcessingResult> RetryOrFailAsync(
        OutboxMessage message,
        Alert? alert,
        string leaseOwner,
        DateTimeOffset now,
        DispatchWorkerOptions workerOptions,
        string category,
        CancellationToken cancellationToken)
    {
        if (message.AttemptCount >= workerOptions.MaxAttempts)
        {
            return await PermanentlyFailAsync(message, alert, leaseOwner, now, category, cancellationToken);
        }

        message.ScheduleRetry(leaseOwner, now, now.Add(workerOptions.RetryDelay), category);
        if (alert is not null)
        {
            AddAudit(alert.OrganizationId, alert.Id.Value, "dispatch.retry-scheduled", "succeeded", $"dispatch:{message.Id.Value:N}", now, new
            {
                reason = category,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DispatchProcessingResult(false, true, false, message.Id.Value, "rescheduled");
    }

    private Task<Alert?> FindAlertForFailureAsync(
        OutboxMessage message,
        CancellationToken cancellationToken)
        => db.Alerts.SingleOrDefaultAsync(
            item => item.OrganizationId == message.OrganizationId && item.Id == new AlertId(message.AggregateId),
            cancellationToken);

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
                    default:
                        throw new DispatchValidationException("payload-invalid", "The dispatch payload contains an unsupported field.");
                }
            }

            if (names.Count != 2 || alertId is null || draftVersion is null || draftVersion <= 0)
            {
                throw new DispatchValidationException("payload-invalid", "The dispatch payload requires an alert ID and draft version.");
            }

            if (alertId.Value != message.AggregateId)
            {
                throw new DispatchValidationException("payload-mismatch", "The dispatch payload does not match its outbox aggregate.");
            }

            return new ParsedDispatchPayload(new AlertId(alertId.Value), draftVersion.Value);
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
        attempt.SetProviderReference(providerReference);
        switch (normalized.Status)
        {
            case DeliveryAttemptStatus.Submitted:
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

    private sealed record ParsedDispatchPayload(AlertId AlertId, int DraftVersion);

    private sealed record RecipientProcessingResult(bool RetryRequested, DateTimeOffset? RetryAtUtc)
    {
        public static RecipientProcessingResult None { get; } = new(false, null);
    }
}
