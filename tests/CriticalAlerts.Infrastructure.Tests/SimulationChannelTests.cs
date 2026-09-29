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
