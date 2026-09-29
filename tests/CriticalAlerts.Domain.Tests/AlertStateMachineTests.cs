using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Domain.Simulation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AlertStateMachineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T16:00:00Z");

    [Fact]
    public void DuplicateRecipientReplacementFailsBeforeMutation()
    {
        var alert = CreateAlert();
        var before = alert.DraftVersion;
        var practitioner = CreatePractitioner(alert.OrganizationId);

        var act = () => alert.ReplaceRecipients(
            [
                RecipientSelection(practitioner, NotificationChannel.SecureMessage),
                RecipientSelection(practitioner, NotificationChannel.SecureMessage),
            ],
            alert.CreatedByUserId,
            before,
            Now);

        act.Should().Throw<DuplicateRecipientException>();
        alert.DraftVersion.Should().Be(before);
        alert.CurrentRecipients.Should().BeEmpty();
    }

    [Fact]
    public void EmptyRecipientReplacementClearsCurrentRecipients()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);
        alert.ReplaceRecipients(
            [RecipientSelection(practitioner, NotificationChannel.SecureMessage)],
            alert.CreatedByUserId,
            alert.DraftVersion,
            Now);
        var before = alert.DraftVersion;

        alert.ReplaceRecipients([], alert.CreatedByUserId, before, Now);

        alert.DraftVersion.Should().Be(before.Next());
        alert.CurrentRecipients.Should().BeEmpty();
        alert.RecipientSelections.Should().ContainSingle(item => item.AlertVersion == before);
    }

    [Fact]
    public void ContentAndApprovedMessageEditsCarryRecipientsToTheNewVersion()
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);
        alert.ReplaceRecipients(
            [RecipientSelection(practitioner, NotificationChannel.SecureMessage)],
            alert.CreatedByUserId,
            alert.DraftVersion,
            Now);
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);
        alert.ConfirmCriticalField("heartRate", "118", "118", "beats/min", alert.CreatedByUserId, alert.DraftVersion, Now);

        alert.UpdateTypedContent(
            "North Wing / Simulation Room 205",
            "Emergent",
            Protect("SIMULATION: revised typed source"),
            Protect("{\"situation\":\"revised\"}"),
            alert.DraftVersion,
            Now);

        var contentVersion = alert.DraftVersion;
        alert.CurrentRecipients.Should().ContainSingle(item =>
            item.AlertVersion == contentVersion
            && item.PractitionerId == practitioner.Id
            && item.Channel == NotificationChannel.SecureMessage);
        alert.FieldConfirmations.Should().ContainSingle(item =>
            item.AlertVersion == contentVersion
            && item.Status == FieldConfirmationStatus.Unresolved);

        alert.SetApprovedMessage(Protect("SIMULATION: approved message"), contentVersion, Now);

        alert.CurrentRecipients.Should().ContainSingle(item =>
            item.AlertVersion == alert.DraftVersion
            && item.PractitionerId == practitioner.Id
            && item.Channel == NotificationChannel.SecureMessage);
        alert.FieldConfirmations.Should().Contain(item =>
            item.AlertVersion == alert.DraftVersion
            && item.Status == FieldConfirmationStatus.Unresolved);
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
    public void ConfirmationRequiresAnApprovedMessage()
    {
        var (alert, practitioner) = CreatePendingAlert(includeApprovedMessage: false);

        var act = () => alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        act.Should().Throw<DomainException>();
        alert.State.Should().Be(AlertState.PendingConfirmation);
        alert.PendingDispatchRequests.Should().BeEmpty();
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
    public void UnresolvedCriticalFieldBlocksConfirmation()
    {
        var (alert, practitioner) = CreatePendingAlert();
        alert.RegisterUnresolvedCriticalField("heartRate", "118", "beats/min", alert.DraftVersion);

        var act = () => alert.ConfirmForDispatch(UserId.New(), alert.DraftVersion, [practitioner], Now, "corr-1");

        act.Should().Throw<UnresolvedCriticalFieldException>();
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
    public void CriticalFieldConfirmationRequiresTheRecordedValueAndUnit()
    {
        var alert = CreateAlert();
        alert.RegisterUnresolvedCriticalField("bloodPressure", "88/54", "mmHg", alert.DraftVersion);

        var changedValue = () => alert.ConfirmCriticalField(
            "bloodPressure",
            "86/52",
            "86/52",
            "mmHg",
            UserId.New(),
            alert.DraftVersion,
            Now);
        var changedUnit = () => alert.ConfirmCriticalField(
            "bloodPressure",
            "88/54",
            "88/54",
            "kPa",
            UserId.New(),
            alert.DraftVersion,
            Now);

        changedValue.Should().Throw<DomainException>();
        changedUnit.Should().Throw<DomainException>();
        alert.FieldConfirmations.Should().ContainSingle(confirmation =>
            confirmation.AlertVersion == alert.DraftVersion
            && confirmation.OriginalValue == "88/54"
            && confirmation.Unit == "mmHg"
            && confirmation.Status == FieldConfirmationStatus.Unresolved);
    }

    [Fact]
    public void ConfirmedCriticalFieldCannotBeRewrittenWithinTheSameDraftVersion()
    {
        var alert = CreateAlert();
        alert.RegisterUnresolvedCriticalField("bloodPressure", "88/54", "mmHg", alert.DraftVersion);
        alert.ConfirmCriticalField(
            "bloodPressure",
            "88/54",
            "88/54",
            "mmHg",
            UserId.New(),
            alert.DraftVersion,
            Now);

        var act = () => alert.ConfirmCriticalField(
            "bloodPressure",
            "88/54",
            "86/52",
            "mmHg",
            UserId.New(),
            alert.DraftVersion,
            Now);

        act.Should().Throw<DomainException>();
        alert.FieldConfirmations.Should().ContainSingle(confirmation =>
            confirmation.NormalizedValue == "88/54"
            && confirmation.Status == FieldConfirmationStatus.Confirmed);
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
            ProtectPatient("HOSP-PAT-0001"),
            "North Wing / Sim Unit 2 / Room 204",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: fictional note for workflow test."),
            Now);

        act.Should().Throw<DomainException>().WithMessage("*SimulationEnvironmentPolicy*");
        SimulationEnvironmentPolicy.SyntheticPatientReferencePrefix.Should().Be("SIM-");
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

    private static (Alert Alert, Practitioner Practitioner) CreatePendingAlert(bool includeApprovedMessage = true)
    {
        var alert = CreateAlert();
        var practitioner = CreatePractitioner(alert.OrganizationId);
        alert.ReplaceRecipients(
            [RecipientSelection(practitioner, NotificationChannel.SecureMessage)],
            alert.CreatedByUserId,
            alert.DraftVersion,
            Now);
        if (includeApprovedMessage)
        {
            alert.SetApprovedMessage(Protect("SIMULATION: approved alert message"), alert.DraftVersion, Now);
        }

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
            ProtectPatient("SIM-PAT-0001"),
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

    private static ValidatedRecipientSelection RecipientSelection(
        Practitioner practitioner,
        NotificationChannel channel)
        => new(
            practitioner.Id,
            null,
            channel,
            "SIM-REVISION-0001",
            Now,
            "On-call not displayed");

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

    private static ProtectedValue ProtectPatient(string text)
        => new(System.Text.Encoding.UTF8.GetBytes(text), "test-v1", ProtectedValuePurposes.AlertPatientReference);
}
