using CriticalAlerts.Application.Dispatch;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class DispatchContractTests
{
    [Fact]
    public void DispatchRequestUsesOpaqueReferencesAndNoClinicalPayload()
    {
        var request = new NotificationDispatchRequest(
            new OrganizationId(Guid.Parse("11111111-1111-4111-8111-111111111111")),
            new AlertId(Guid.Parse("22222222-2222-4222-8222-222222222222")),
            new AlertDraftVersion(4),
            new AlertRecipientSelectionId(Guid.Parse("33333333-3333-4333-8333-333333333333")),
            NotificationChannel.Sms,
            "SIM-ENDPOINT-MAYA-SMS",
            "alert:22222222-2222-4222-8222-222222222222:v4",
            "SIMULATION: please open the secure alert application.",
            "attempt-key",
            "corr-key");

        request.EndpointReference.Should().Be("SIM-ENDPOINT-MAYA-SMS");
        request.MessageReference.Should().StartWith("alert:");
        request.WakeUpText.Should().StartWith("SIMULATION:");
        typeof(NotificationDispatchRequest).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(new[] { "Body", "ClinicalPayload", "RawEndpoint", "Ciphertext", "ProviderUrl" });
    }

    [Fact]
    public void DeliveryStatusViewContainsOnlyOperationalDeliveryState()
    {
        var view = new DeliveryStatusView(
            Guid.NewGuid(),
            4,
            "Active",
            "Pending",
            [new DeliveryAttemptView(
                Guid.NewGuid(),
                "SecureMessage",
                1,
                "simulation-secure-message",
                "Delivered",
                "PendingNotObserved",
                DateTimeOffset.Parse("2026-08-29T12:00:00Z"),
                DateTimeOffset.Parse("2026-08-29T12:00:01Z"),
                DateTimeOffset.Parse("2026-08-29T12:00:02Z"),
                null,
                null)]);

        view.Attempts.Should().ContainSingle();
        typeof(DeliveryStatusView).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(new[] { "PatientReference", "ApprovedMessage", "Endpoint", "Source", "Payload" });
    }
}
