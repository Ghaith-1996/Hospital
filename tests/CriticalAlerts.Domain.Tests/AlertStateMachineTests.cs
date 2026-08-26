using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Domain.Simulation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AlertStateMachineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T16:00:00Z");

    [Fact]
    public void CreateDraftStartsInDraft()
    {
        var alert = CreateAlert(includeStructuredContent: false);

        alert.State.Should().Be(AlertState.Draft);
        alert.DraftVersion.Should().Be(AlertDraftVersion.Initial);
        alert.ConfirmedDraftVersion.Should().BeNull();
    }

    [Fact]
    public void TypedContentEditIncrementsTheDraftVersion()
    {
        var alert = CreateAlert();
        var previous = alert.DraftVersion;

        alert.UpdateTypedContent(
            "North Wing / Simulation Room 205",
            "Emergent",
            Protect("SIMULATION: revised typed source"),
            Protect("{\"situation\":\"revised\"}"),
            previous,
            Now);

        alert.DraftVersion.Should().Be(previous.Next());
        alert.State.Should().Be(AlertState.Draft);
    }

    [Fact]
    public void SubmitRequiresStructuredTypedContent()
    {
        var alert = CreateAlert(includeStructuredContent: false);

        var act = () => alert.SubmitForConfirmation(alert.CreatedByUserId, alert.DraftVersion, Now);

        act.Should().Throw<DomainException>();
        alert.State.Should().Be(AlertState.Draft);
    }

    [Fact]
    public void UnresolvedCriticalFieldsBlockSubmission()
    {
        var alert = CreateAlert();
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);

        var act = () => alert.SubmitForConfirmation(alert.CreatedByUserId, alert.DraftVersion, Now);

        act.Should().Throw<UnresolvedCriticalFieldException>();
        alert.State.Should().Be(AlertState.Draft);
    }

    [Fact]
    public void SubmitThenConfirmIsAllowed()
    {
        var (alert, practitioner) = CreatePendingAlert();
        alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        alert.State.Should().Be(AlertState.DispatchQueued);
        alert.ConfirmedDraftVersion.Should().Be(alert.DraftVersion);
        alert.PendingDispatchRequests.Should().ContainSingle();
        alert.HasReusableApprovalForCurrentVersion.Should().BeTrue();
    }

    [Fact]
    public void ConfirmFromDraftIsRejected()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);

        var act = () => alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        act.Should().Throw<InvalidAlertTransitionException>();
        alert.PendingDispatchRequests.Should().BeEmpty();
    }

    [Fact]
    public void ConfirmWithZeroRecipientsIsRejected()
    {
        var alert = CreateAlert();
        alert.SubmitForConfirmation(alert.CreatedByUserId, alert.DraftVersion, Now);

        var act = () => alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [], Now, "corr-1");

        act.Should().Throw<RecipientsRequiredException>();
    }

    [Fact]
    public void StaleVersionIsRejected()
    {
        var alert = CreateAlert();
        var previous = alert.DraftVersion;
        alert.UpdateSource(Protect("SIMULATION: revised source"), previous, Now);

        var act = () => alert.SubmitForConfirmation(alert.CreatedByUserId, previous, Now);

        act.Should().Throw<StaleAlertVersionException>();
    }

    [Fact]
    public void EditInvalidatesApprovalAndReturnsToDraft()
    {
        var (alert, practitioner) = CreatePendingAlert();
        var versionBeforeConfirm = alert.DraftVersion;
        alert.ConfirmForDispatch(UserId.New(), versionBeforeConfirm, [practitioner], Now, "corr-1");

        var act = () => alert.UpdateSource(Protect("SIMULATION: edited after confirm"), alert.DraftVersion, Now);

        act.Should().Throw<InvalidAlertTransitionException>();
        alert.State.Should().Be(AlertState.DispatchQueued);
        alert.HasReusableApprovalForCurrentVersion.Should().BeTrue();
    }

    [Fact]
    public void PendingConfirmationEditClearsApprovalAndIncrementsVersion()
    {
        var (alert, _) = CreatePendingAlert();
        var previous = alert.DraftVersion;
        alert.UpdateSource(Protect("SIMULATION: correction"), previous, Now);

        alert.State.Should().Be(AlertState.Draft);
        alert.DraftVersion.Should().NotBe(previous);
        alert.ConfirmedDraftVersion.Should().BeNull();
        alert.PendingDispatchRequests.Should().BeEmpty();
    }

    [Fact]
    public void ConfirmOfOlderVersionAfterEditIsRejected()
    {
        var (alert, practitioner) = CreatePendingAlert();
        var oldVersion = alert.DraftVersion;
        alert.UpdateSource(Protect("SIMULATION: newer source"), oldVersion, Now);

        var act = () => alert.ConfirmForDispatch(UserId.New(), oldVersion, [practitioner], Now, "corr-1");

        act.Should().Throw<StaleAlertVersionException>();
        alert.HasReusableApprovalForCurrentVersion.Should().BeFalse();
    }

    [Fact]
    public void CancelAfterResolveIsRejected()
    {
        var (alert, practitioner) = CreatePendingAlert();
        alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");
        alert.MarkActive(Now, "corr-2");
        alert.Resolve(UserId.New(), Now, "corr-3");

        var act = () => alert.Cancel(UserId.New(), Now, "corr-4");

        act.Should().Throw<InvalidAlertTransitionException>();
        alert.State.Should().Be(AlertState.Resolved);
    }

    [Theory]
    [InlineData(AlertState.DispatchQueued, AlertState.Draft)]
    [InlineData(AlertState.Resolved, AlertState.Active)]
    [InlineData(AlertState.Cancelled, AlertState.Draft)]
    [InlineData(AlertState.Failed, AlertState.Resolved)]
    [InlineData(AlertState.Draft, AlertState.DispatchQueued)]
    [InlineData(AlertState.Draft, AlertState.Active)]
    [InlineData(AlertState.PendingConfirmation, AlertState.Active)]
    public void ProhibitedTransitionsAreRejected(AlertState from, AlertState to)
    {
        AlertStateMachine.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public void AllowedPhase0TransitionsAreAccepted()
    {
        AlertStateMachine.AllowedTransitions.Should().Contain((AlertState.Draft, AlertState.PendingConfirmation));
        AlertStateMachine.AllowedTransitions.Should().Contain((AlertState.PendingConfirmation, AlertState.DispatchQueued));
        AlertStateMachine.AllowedTransitions.Should().Contain((AlertState.DispatchQueued, AlertState.Active));
        AlertStateMachine.AllowedTransitions.Should().Contain((AlertState.Active, AlertState.Resolved));
        AlertStateMachine.AllowedTransitions.Should().Contain((AlertState.Failed, AlertState.Active));
    }

    [Fact]
    public void UnresolvedCriticalFieldBlocksConfirmation()
    {
        var (alert, practitioner) = CreatePendingAlert();
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);

        var act = () => alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        act.Should().Throw<UnresolvedCriticalFieldException>();
    }

    [Fact]
    public void ConfirmedCriticalFieldAllowsDispatch()
    {
        var (alert, practitioner) = CreatePendingAlert();
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", UserId.New(), alert.DraftVersion, Now);
        alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        alert.State.Should().Be(AlertState.DispatchQueued);
        alert.FieldConfirmations.Should().ContainSingle(confirmation => confirmation.Status == FieldConfirmationStatus.Confirmed);
    }

    [Fact]
    public void FieldConfirmationIsCanonicalPerAlertVersionAndField()
    {
        var alert = CreateAlert();
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", UserId.New(), alert.DraftVersion, Now);

        alert.FieldConfirmations.Should().ContainSingle(confirmation => confirmation.FieldId == "heartRate");
        alert.FieldConfirmations.Single().Status.Should().Be(FieldConfirmationStatus.Confirmed);
        alert.FieldConfirmations.Single().AlertVersion.Should().Be(alert.DraftVersion);
    }

    [Fact]
    public void SimulationPatientReferenceIsSimulationEnvironmentPolicy()
    {
        var act = () => Alert.CreateDraft(
            AlertId.New(),
            OrganizationId.New(),
            SiteId.New(),
            DepartmentId.New(),
            UserId.New(),
            "HOSP-PAT-0001",
            "North Wing / Sim Unit 2 / Room 204",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: fictional note for workflow test."),
            Now);

        act.Should().Throw<DomainException>().WithMessage("*SimulationEnvironmentPolicy*");
        SimulationEnvironmentPolicy.SyntheticPatientReferencePrefix.Should().Be("SIM-");
    }

    [Fact]
    public void DuplicateRecipientIsRejected()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);
        alert.SelectRecipient(practitioner, null, NotificationChannel.SecureMessage, UserId.New(), alert.DraftVersion, Now);

        var act = () => alert.SelectRecipient(practitioner, null, NotificationChannel.SecureMessage, UserId.New(), alert.DraftVersion, Now);

        act.Should().Throw<DuplicateRecipientException>();
    }

    [Fact]
    public void InactivePractitionerCannotBeSelected()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId, isActive: false);

        var act = () => alert.SelectRecipient(practitioner, null, NotificationChannel.SecureMessage, UserId.New(), alert.DraftVersion, Now);

        act.Should().Throw<InactivePractitionerException>();
    }

    [Fact]
    public void CrossOrganizationRecipientIsRejected()
    {
        var alert = CreateAlert();
        var foreign = CreatePractitioner(OrganizationId.New());

        var act = () => alert.SelectRecipient(foreign, null, NotificationChannel.SecureMessage, UserId.New(), alert.DraftVersion, Now);

        act.Should().Throw<OrganizationIsolationException>();
    }

    [Fact]
    public void AcknowledgementDoesNotCreateResponsibility()
    {
        var response = RecipientResponse.Record(
            RecipientResponseId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            RecipientResponseType.Acknowledged,
            UserId.New(),
            Now,
            "acknowledged");

        response.ImpliesResponsibilityAcceptance.Should().BeFalse();
        ResponsibilityAssignment.FromResponse(response, PractitionerId.New()).Should().BeNull();
    }

    [Fact]
    public void AcceptanceCreatesResponsibilityAssignment()
    {
        var practitionerId = PractitionerId.New();
        var response = RecipientResponse.Record(
            RecipientResponseId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            RecipientResponseType.Accepted,
            UserId.New(),
            Now,
            "accepted");

        var assignment = ResponsibilityAssignment.FromResponse(response, practitionerId);

        assignment.Should().NotBeNull();
        assignment!.PractitionerId.Should().Be(practitionerId);
    }

    [Fact]
    public void AuditEventIsAppendOriented()
    {
        var audit = AuditEvent.Record(
            AuditEventId.New(),
            OrganizationId.New(),
            "user",
            UserId.New(),
            "alert.confirmed",
            "alert",
            Guid.NewGuid(),
            "succeeded",
            "corr-9",
            """{"draftVersion":2}""",
            Now);

        audit.Action.Should().Be("alert.confirmed");
        audit.OccurredAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void OutboxRejectsClinicalPayloads()
    {
        var act = () => OutboxMessage.Create(
            OutboxMessageId.New(),
            OrganizationId.New(),
            "AlertDispatchRequested",
            Guid.NewGuid(),
            """{"note":"patient SIM-PAT-0001 HR 118 beats/min"}""",
            "key-1",
            Now);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void OutboxAcceptsIdentifierPayloads()
    {
        var message = OutboxMessage.Create(
            OutboxMessageId.New(),
            OrganizationId.New(),
            "AlertDispatchRequested",
            Guid.NewGuid(),
            """{"alertId":"11111111-1111-1111-1111-111111111111","draftVersion":2}""",
            "key-1",
            Now);

        message.ProcessingState.Should().Be(OutboxProcessingState.Pending);
    }

    [Fact]
    public void NonUtcTimestampsAreRejected()
    {
        var act = () => Organization.CreateSimulation(
            OrganizationId.New(),
            "Fictional Harborview Simulation Hospital",
            DateTimeOffset.Parse("2026-08-19T12:00:00-04:00"));

        act.Should().Throw<NonUtcTimestampException>();
    }

    [Fact]
    public void DeliveryIsNotAcknowledgement()
    {
        var attempt = DeliveryAttempt.CreateRequested(
            DeliveryAttemptId.New(),
            OrganizationId.New(),
            AlertId.New(),
            AlertRecipientSelectionId.New(),
            NotificationChannel.Sms,
            1,
            "delivery-1",
            "simulation",
            Now);
        attempt.MarkDelivered(Now);

        attempt.Status.Should().Be(DeliveryAttemptStatus.Delivered);
        attempt.OpenedState.Should().Be(ObservationState.NotApplicable);
    }

    private static (Alert Alert, Practitioner Practitioner) CreatePendingAlert()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);
        alert.SelectRecipient(practitioner, null, NotificationChannel.SecureMessage, UserId.New(), alert.DraftVersion, Now);
        alert.SubmitForConfirmation(alert.CreatedByUserId, alert.DraftVersion, Now);
        return (alert, practitioner);
    }

    private static Alert CreateAlert(bool includeStructuredContent = true)
    {
        var alert = Alert.CreateDraft(
            AlertId.New(),
            OrganizationId.New(),
            SiteId.New(),
            DepartmentId.New(),
            UserId.New(),
            "SIM-PAT-0001",
            "North Wing / Sim Unit 2 / Room 204",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: fictional note for workflow test."),
            Now);
        if (includeStructuredContent)
        {
            alert.SetStructuredSuggestion(Protect("{\"situation\":\"fictional workflow\"}"), alert.DraftVersion, Now);
        }

        return alert;
    }

    private static Practitioner CreatePractitioner(OrganizationId organizationId, bool isActive = true)
    {
        return Practitioner.Create(
            PractitionerId.New(),
            organizationId,
            "Maya",
            "Chen",
            "SIM-PRAC-0101",
            "Emergency",
            isActive,
            Now);
    }

    private static ProtectedValue Protect(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text), "test-v1", "alert-source");
}
