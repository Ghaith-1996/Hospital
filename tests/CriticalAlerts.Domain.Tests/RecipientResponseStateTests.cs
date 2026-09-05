using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class RecipientResponseStateTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T14:00:00Z");

    [Fact]
    public void PractitionerUserLinkRejectsNonUtcCreationTime()
    {
        var localTime = new DateTimeOffset(2026, 8, 30, 10, 0, 0, TimeSpan.FromHours(-4));

        var act = () => PractitionerUserLink.Create(
            PractitionerUserLinkId.New(),
            OrganizationId.New(),
            UserId.New(),
            PractitionerId.New(),
            localTime);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SecureMessageOpenRecordsTheFirstUtcObservation()
    {
        var attempt = CreateAttempt(NotificationChannel.SecureMessage);

        attempt.MarkOpened(Now);
        attempt.MarkOpened(Now.AddMinutes(2));

        attempt.OpenedState.Should().Be(ObservationState.Occurred);
        attempt.OpenedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void NonSecureMessageOpenRemainsNotApplicable()
    {
        var attempt = CreateAttempt(NotificationChannel.Sms);

        attempt.MarkOpened(Now);

        attempt.OpenedState.Should().Be(ObservationState.NotApplicable);
        attempt.OpenedAtUtc.Should().BeNull();
    }

    [Fact]
    public void AcknowledgementDoesNotCreateResponsibility()
    {
        var response = Record(RecipientResponseType.Acknowledged, "simulation-acknowledged");

        response.IsAcknowledgement.Should().BeTrue();
        response.IsTerminalDisposition.Should().BeFalse();
        response.Category.Should().Be(RecipientResponseCategory.Acknowledgement);
        ResponsibilityAssignment.FromResponse(response).Should().BeNull();
    }

    [Fact]
    public void AcceptanceCreatesResponsibilityForTheExactPractitionerAndVersion()
    {
        var response = Record(RecipientResponseType.Accepted, "simulation-responsibility-accepted");

        var assignment = ResponsibilityAssignment.FromResponse(response);

        assignment.Should().NotBeNull();
        assignment!.PractitionerId.Should().Be(response.PractitionerId);
        assignment.AlertVersion.Should().Be(response.AlertVersion);
        assignment.SourceResponseId.Should().Be(response.Id);
        assignment.AcceptedAtUtc.Should().Be(response.OccurredAtUtc);
    }

    [Theory]
    [InlineData(RecipientResponseType.Declined)]
    [InlineData(RecipientResponseType.Unavailable)]
    public void NonAcceptedDispositionDoesNotCreateResponsibility(RecipientResponseType responseType)
    {
        var response = Record(responseType, $"simulation-{responseType.ToString().ToLowerInvariant()}");

        response.IsTerminalDisposition.Should().BeTrue();
        response.Category.Should().Be(RecipientResponseCategory.TerminalDisposition);
        ResponsibilityAssignment.FromResponse(response).Should().BeNull();
    }

    [Fact]
    public void ResponseRejectsCallerFreeTextAsAReasonCode()
    {
        var act = () => Record(RecipientResponseType.Declined, "I am unavailable because this contains free text");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ResponseRejectsAReasonCodeForAnotherAction()
    {
        var act = () => Record(RecipientResponseType.Declined, "simulation-unavailable");

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void CallUnitRequestIsAnAcknowledgementWithoutResponsibilityAcceptance()
    {
        var response = Record(RecipientResponseType.CallUnitRequested, "simulation-call-unit-requested");

        response.IsCallUnitRequest.Should().BeTrue();
        response.IsTerminalDisposition.Should().BeFalse();
        response.Category.Should().Be(RecipientResponseCategory.CallUnitRequest);
        ResponsibilityAssignment.FromResponse(response).Should().BeNull();
    }

    private static RecipientResponse Record(RecipientResponseType responseType, string reasonCode)
        => RecipientResponse.Record(
            RecipientResponseId.New(),
            OrganizationId.New(),
            AlertId.New(),
            new AlertDraftVersion(7),
            PractitionerId.New(),
            responseType,
            UserId.New(),
            Now,
            reasonCode);

    private static DeliveryAttempt CreateAttempt(NotificationChannel channel)
        => DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            channel,
            1,
            $"simulation-{channel.ToString().ToLowerInvariant()}-attempt",
            "simulation-provider",
            Now.AddMinutes(-5));
}
