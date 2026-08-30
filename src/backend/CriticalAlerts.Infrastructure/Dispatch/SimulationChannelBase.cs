using System.Security.Cryptography;
using System.Text;
using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Infrastructure.Dispatch;

public abstract class SimulationChannelBase(TimeProvider time) : INotificationChannel
{
    public abstract NotificationChannel ChannelType { get; }

    public abstract string ProviderName { get; }

    public Task<NotificationDispatchResult> DispatchAsync(
        NotificationDispatchRequest request,
        SimulationDispatchScenario scenario,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        if (!Enum.IsDefined(scenario))
        {
            throw new DispatchValidationException("scenario-invalid", "The simulation dispatch scenario is not supported.");
        }

        var now = time.GetUtcNow();
        if (now.Offset != TimeSpan.Zero)
        {
            throw new DispatchValidationException("clock-not-utc", "Simulation dispatch requires a UTC clock.");
        }

        var providerReference = CreateProviderReference(request.IdempotencyKey);
        var result = scenario switch
        {
            SimulationDispatchScenario.DelayedDelivery => DelayedDelivery(request, providerReference, now),
            SimulationDispatchScenario.SmsFailure when ChannelType == NotificationChannel.Sms
                => Failure(request, providerReference, now, "sms-failure"),
            SimulationDispatchScenario.VoiceNoAnswer when ChannelType == NotificationChannel.Voice
                => Failure(request, providerReference, now, "voice-no-answer"),
            SimulationDispatchScenario.ProviderOutage => ProviderOutage(request, providerReference, now),
            SimulationDispatchScenario.DuplicateCallback => DuplicateCallback(providerReference, now),
            SimulationDispatchScenario.OutOfOrderCallback => OutOfOrderCallback(providerReference, now),
            _ => ImmediateSuccess(providerReference, now),
        };

        return Task.FromResult(result);
    }

    private static NotificationDispatchResult ImmediateSuccess(string providerReference, DateTimeOffset now)
        => new(
            providerReference,
            [
                Event(providerReference, "submitted", now),
                Event(providerReference, "delivered", now.AddSeconds(1)),
            ],
            Retryable: false,
            FailureCategory: null);

    private static NotificationDispatchResult DelayedDelivery(
        NotificationDispatchRequest request,
        string providerReference,
        DateTimeOffset now)
    {
        if (request.CurrentAttemptStatus == DeliveryAttemptStatus.Submitted)
        {
            return new NotificationDispatchResult(
                providerReference,
                [Event(providerReference, "delivered", now)],
                Retryable: false,
                FailureCategory: null);
        }

        if (request.CurrentAttemptStatus == DeliveryAttemptStatus.Delivered)
        {
            return new NotificationDispatchResult(providerReference, [], Retryable: false, FailureCategory: null);
        }

        return new NotificationDispatchResult(
            providerReference,
            [Event(providerReference, "submitted", now)],
            Retryable: true,
            FailureCategory: null,
            RetryAtUtc: now.AddSeconds(1));
    }

    private static NotificationDispatchResult Failure(
        NotificationDispatchRequest request,
        string providerReference,
        DateTimeOffset now,
        string failureCategory)
        => new(
            providerReference,
            [Event(providerReference, "failed", now, failureCategory)],
            Retryable: false,
            FailureCategory: failureCategory);

    private static NotificationDispatchResult ProviderOutage(
        NotificationDispatchRequest request,
        string providerReference,
        DateTimeOffset now)
        => new(
            providerReference,
            [Event(providerReference, "failed", now, "provider-unavailable")],
            Retryable: true,
            FailureCategory: "provider-unavailable",
            RetryAtUtc: now.AddSeconds(1));

    private static NotificationDispatchResult DuplicateCallback(string providerReference, DateTimeOffset now)
        => new(
            providerReference,
            [
                Event(providerReference, "submitted", now),
                Event(providerReference, "delivered", now.AddSeconds(1)),
                Event(providerReference, "delivered", now.AddSeconds(1)),
            ],
            Retryable: false,
            FailureCategory: null);

    private static NotificationDispatchResult OutOfOrderCallback(string providerReference, DateTimeOffset now)
        => new(
            providerReference,
            [
                Event(providerReference, "delivered", now.AddSeconds(1)),
                Event(providerReference, "submitted", now),
            ],
            Retryable: false,
            FailureCategory: null);

    private static NotificationProviderEvent Event(
        string providerReference,
        string eventType,
        DateTimeOffset occurredAtUtc,
        string? failureCategory = null)
        => new(
            $"{providerReference}:{eventType}",
            eventType,
            occurredAtUtc,
            "simulation",
            failureCategory);

    private string CreateProviderReference(string idempotencyKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ProviderName}|{idempotencyKey}")));
        return $"SIM-PROVIDER-{hash[..20]}";
    }

    private void ValidateRequest(NotificationDispatchRequest request)
    {
        if (request.Channel != ChannelType
            || request.OrganizationId.Value == Guid.Empty
            || request.AlertId.Value == Guid.Empty
            || request.RecipientSelectionId.Value == Guid.Empty
            || request.DraftVersion.Value <= 0)
        {
            throw new DispatchValidationException("request-invalid", "The simulation dispatch request is invalid.");
        }

        if (!IsSafeSimulationReference(request.EndpointReference, "SIM-")
            || !IsSafeSimulationReference(request.MessageReference, "alert:")
            || !IsSafeSimulationReference(request.IdempotencyKey, "alert-dispatch:")
            || !IsSafeSimulationReference(request.CorrelationId, "dispatch:"))
        {
            throw new DispatchValidationException("reference-invalid", "Simulation dispatch requires opaque synthetic references.");
        }

        if (string.IsNullOrWhiteSpace(request.WakeUpText)
            || !request.WakeUpText.StartsWith("SIMULATION:", StringComparison.Ordinal)
            || request.WakeUpText.Length > 240)
        {
            throw new DispatchValidationException("wake-up-text-invalid", "Simulation wake-up text must be generic synthetic content.");
        }
    }

    private static bool IsSafeSimulationReference(string value, string prefix)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 200
            && value.StartsWith(prefix, StringComparison.Ordinal)
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_');
}
