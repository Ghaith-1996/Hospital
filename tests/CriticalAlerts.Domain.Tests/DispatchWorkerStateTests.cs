using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Reliability;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class DispatchWorkerStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

    [Fact]
    public void OutboxLeaseIsOwnedAndCanBeReclaimedAfterExpiry()
    {
        var message = CreateOutbox();

        message.TryAcquireLease("worker-a", Now, Now.AddMinutes(1)).Should().BeTrue();
        message.TryAcquireLease("worker-b", Now.AddSeconds(10), Now.AddMinutes(2)).Should().BeFalse();

        message.TryAcquireLease("worker-b", Now.AddMinutes(1), Now.AddMinutes(3)).Should().BeTrue();
        message.LeaseOwner.Should().Be("worker-b");
        message.AttemptCount.Should().Be(2);
    }

    [Fact]
    public void OutboxCompletionRequiresCurrentLeaseOwner()
    {
        var message = CreateOutbox();
        message.TryAcquireLease("worker-a", Now, Now.AddMinutes(1)).Should().BeTrue();

        var act = () => message.MarkProcessed("worker-b", Now.AddSeconds(1));

        act.Should().Throw<DomainException>().WithMessage("*lease owner*");
        message.ProcessingState.Should().Be(OutboxProcessingState.Processing);

        message.MarkProcessed("worker-a", Now.AddSeconds(1));
        message.ProcessingState.Should().Be(OutboxProcessingState.Processed);
        message.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void OutboxRetryClearsLeaseAndUsesUtcDueTime()
    {
        var message = CreateOutbox();
        message.TryAcquireLease("worker-a", Now, Now.AddMinutes(1)).Should().BeTrue();

        message.ScheduleRetry("worker-a", Now, Now.AddSeconds(30), "provider-unavailable");

        message.ProcessingState.Should().Be(OutboxProcessingState.Pending);
        message.NextAttemptAtUtc.Should().Be(Now.AddSeconds(30));
        message.LastErrorCategory.Should().Be("provider-unavailable");
        message.LeaseOwner.Should().BeNull();
        message.LeaseExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public void DeliveryAttemptDoesNotRegressWhenEventsArriveOutOfOrder()
    {
        var attempt = DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            NotificationChannel.SecureMessage,
            attemptNumber: 1,
            "attempt-key",
            "simulation-secure-message",
            Now);

        attempt.SetProviderReference("SIM-PROVIDER-MSG-0001");
        attempt.MarkDelivered(Now.AddSeconds(2));
        attempt.MarkSubmitted("SIM-PROVIDER-MSG-0001", Now.AddSeconds(3));

        attempt.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        attempt.DeliveredAtUtc.Should().Be(Now.AddSeconds(2));
        attempt.SubmittedAtUtc.Should().BeNull();
    }

    [Fact]
    public void DeliveryFailureIsTerminalForTheAttempt()
    {
        var attempt = DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            NotificationChannel.Voice,
            attemptNumber: 1,
            "attempt-key",
            "simulation-voice",
            Now);

        attempt.MarkFailed("voice-no-answer", Now.AddSeconds(2));
        attempt.MarkDelivered(Now.AddSeconds(3));

        attempt.Status.Should().Be(DeliveryAttemptStatus.Failed);
        attempt.FailureCategory.Should().Be("voice-no-answer");
        attempt.DeliveredAtUtc.Should().BeNull();
    }

    [Fact]
    public void SimulationScenarioCatalogIsFixedAndProviderIndependent()
    {
        Enum.GetValues<SimulationDispatchScenario>()
            .Should().BeEquivalentTo(
            [
                SimulationDispatchScenario.ImmediateSuccess,
                SimulationDispatchScenario.DelayedDelivery,
                SimulationDispatchScenario.SmsFailure,
                SimulationDispatchScenario.VoiceNoAnswer,
                SimulationDispatchScenario.ProviderOutage,
                SimulationDispatchScenario.DuplicateCallback,
                SimulationDispatchScenario.OutOfOrderCallback,
            ]);
    }

    private static OutboxMessage CreateOutbox()
        => OutboxMessage.Create(
            OutboxMessageId.New(),
            OrganizationId.New(),
            "AlertDispatchRequested",
            Guid.NewGuid(),
            "{\"alertId\":\"11111111-1111-4111-8111-111111111999\",\"draftVersion\":1}",
            "outbox-key",
            Now);
}
