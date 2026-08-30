using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Infrastructure.Dispatch;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class SimulationChannelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-29T12:00:00Z");

    [Fact]
    public async Task SmsSimulationUsesGenericSyntheticContentAndProducesDeliveryEvents()
    {
        var channel = new SimulationSmsChannel(new FixedTimeProvider(Now));
        var request = CreateRequest(NotificationChannel.Sms, "SIM-ENDPOINT-MAYA-SMS", "SIMULATION: please open the secure alert application.");

        var result = await channel.DispatchAsync(request, SimulationDispatchScenario.ImmediateSuccess, CancellationToken.None);

        result.Retryable.Should().BeFalse();
        result.Events.Select(item => item.EventType).Should().Equal("submitted", "delivered");
        result.Events.Should().OnlyContain(item => item.SanitizedMetadata.Contains("simulation", StringComparison.OrdinalIgnoreCase));
        result.ProviderReference.Should().StartWith("SIM-PROVIDER-");
        result.Events.Select(item => item.SanitizedMetadata)
            .Should().NotContain(item => item.Contains("SIM-ENDPOINT-MAYA-SMS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DelayedDeliveryKeepsTheSameAttemptAndEmitsDeliveryOnlyAfterSubmission()
    {
        var channel = new SimulationSecureMessageChannel(new FixedTimeProvider(Now));
        var request = CreateRequest(NotificationChannel.SecureMessage, "SIM-ENDPOINT-MAYA-SECURE", "SIMULATION: secure message available.");

        var first = await channel.DispatchAsync(request with { CurrentAttemptStatus = DeliveryAttemptStatus.Requested }, SimulationDispatchScenario.DelayedDelivery, CancellationToken.None);
        var second = await channel.DispatchAsync(request with { CurrentAttemptStatus = DeliveryAttemptStatus.Submitted }, SimulationDispatchScenario.DelayedDelivery, CancellationToken.None);

        first.Events.Select(item => item.EventType).Should().Equal("submitted");
        second.Events.Select(item => item.EventType).Should().Equal("delivered");
        first.ProviderReference.Should().Be(second.ProviderReference);
        first.Events.Single().ProviderEventId.Should().NotBe(second.Events.Single().ProviderEventId);
    }

    [Theory]
    [InlineData(NotificationChannel.Sms, SimulationDispatchScenario.SmsFailure, "sms-failure")]
    [InlineData(NotificationChannel.Voice, SimulationDispatchScenario.VoiceNoAnswer, "voice-no-answer")]
    public async Task ChannelSpecificFailureScenariosAreNormalizedToSafeCategories(
        NotificationChannel channelType,
        SimulationDispatchScenario scenario,
        string failureCategory)
    {
        var channel = CreateChannel(channelType);
        var result = await channel.DispatchAsync(
            CreateRequest(channelType, "SIM-ENDPOINT-FAILURE", "SIMULATION: please open the secure alert application."),
            scenario,
            CancellationToken.None);

        result.Retryable.Should().BeFalse();
        result.FailureCategory.Should().Be(failureCategory);
        result.Events.Should().ContainSingle(item => item.EventType == "failed");
    }

    [Fact]
    public async Task OutOfOrderScenarioReturnsDeliveredBeforeSubmittedWithoutChangingProviderReference()
    {
        var channel = new SimulationVoiceChannel(new FixedTimeProvider(Now));

        var result = await channel.DispatchAsync(
            CreateRequest(NotificationChannel.Voice, "SIM-ENDPOINT-VOICE", "SIMULATION: please open the secure alert application."),
            SimulationDispatchScenario.OutOfOrderCallback,
            CancellationToken.None);

        result.Events.Select(item => item.EventType).Should().Equal("delivered", "submitted");
        result.Events.Select(item => item.ProviderEventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task DuplicateCallbackScenarioUsesTheSameProviderEventIdentifier()
    {
        var channel = new SimulationSecureMessageChannel(new FixedTimeProvider(Now));

        var result = await channel.DispatchAsync(
            CreateRequest(NotificationChannel.SecureMessage, "SIM-ENDPOINT-SECURE", "SIMULATION: secure message available."),
            SimulationDispatchScenario.DuplicateCallback,
            CancellationToken.None);

        result.Events.Select(item => item.ProviderEventId).Should().ContainInOrder(
            result.Events[0].ProviderEventId,
            result.Events[1].ProviderEventId,
            result.Events[1].ProviderEventId);
    }

    private static INotificationChannel CreateChannel(NotificationChannel channel)
        => channel switch
        {
            NotificationChannel.SecureMessage => new SimulationSecureMessageChannel(new FixedTimeProvider(Now)),
            NotificationChannel.Sms => new SimulationSmsChannel(new FixedTimeProvider(Now)),
            NotificationChannel.Voice => new SimulationVoiceChannel(new FixedTimeProvider(Now)),
            _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null),
        };

    private static NotificationDispatchRequest CreateRequest(NotificationChannel channel, string endpointReference, string wakeUpText)
        => new(
            new OrganizationId(Guid.Parse("11111111-1111-4111-8111-111111111111")),
            new AlertId(Guid.Parse("22222222-2222-4222-8222-222222222222")),
            new AlertDraftVersion(4),
            new AlertRecipientSelectionId(Guid.Parse("33333333-3333-4333-8333-333333333333")),
            channel,
            endpointReference,
            "alert:22222222-2222-4222-8222-222222222222:v4",
            wakeUpText,
            "alert-dispatch:attempt-key",
            "dispatch:corr-key");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
