using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Simulation;

namespace CriticalAlerts.Domain.Alerts;

public sealed class Alert
{
    private readonly List<AlertFieldConfirmation> fieldConfirmations = [];
    private readonly List<AlertRecipientSelection> recipientSelections = [];
    private readonly List<AlertStateTransition> stateTransitions = [];
    private readonly List<AlertDispatchRequested> pendingDispatchRequests = [];

    private Alert()
    {
        SimulationPatientReference = string.Empty;
        Location = string.Empty;
        UrgencyLabel = string.Empty;
        DemoEscalationPolicyVersion = string.Empty;
        DemoNotificationPolicyVersion = string.Empty;
    }

    private Alert(
        AlertId id,
        OrganizationId organizationId,
        SiteId siteId,
        DepartmentId departmentId,
        UserId createdByUserId,
        string simulationPatientReference,
        string location,
        string urgencyLabel,
        AlertSourceType sourceType,
        ProtectedValue? originalSource,
        ProtectedValue? structuredSuggestion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        SiteId = siteId;
        DepartmentId = departmentId;
        CreatedByUserId = createdByUserId;
        SimulationPatientReference = simulationPatientReference;
        Location = location;
        UrgencyLabel = urgencyLabel;
        SourceType = sourceType;
        OriginalSource = originalSource;
        StructuredSuggestion = structuredSuggestion;
        State = AlertState.Draft;
        DraftVersion = AlertDraftVersion.Initial;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        DemoEscalationPolicyVersion = "DEMO";
        DemoNotificationPolicyVersion = "DEMO";
        RecordTransition(AlertState.Draft, AlertState.Draft, createdByUserId, "created", "alert-created", "DEMO", createdAtUtc);
    }

    public AlertId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public SiteId SiteId { get; private set; }

    public DepartmentId DepartmentId { get; private set; }

    public UserId CreatedByUserId { get; private set; }

    public string SimulationPatientReference { get; private set; }

    public string Location { get; private set; }

    public string UrgencyLabel { get; private set; }

    public AlertSourceType SourceType { get; private set; }

    public ProtectedValue? OriginalSource { get; private set; }

    public ProtectedValue? Transcription { get; private set; }

    public ProtectedValue? StructuredSuggestion { get; private set; }

    public ProtectedValue? ApprovedMessage { get; private set; }

    public AlertState State { get; private set; }

    public AlertDraftVersion DraftVersion { get; private set; }

    public AlertDraftVersion? ConfirmedDraftVersion { get; private set; }

    public UserId? ConfirmedByUserId { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    public string DemoEscalationPolicyVersion { get; private set; }

    public string DemoNotificationPolicyVersion { get; private set; }

    public UserId? ResolvedByUserId { get; private set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<AlertFieldConfirmation> FieldConfirmations => fieldConfirmations;

    public IReadOnlyCollection<AlertRecipientSelection> RecipientSelections => recipientSelections;

    public IReadOnlyCollection<AlertStateTransition> StateTransitions => stateTransitions;

    public IReadOnlyCollection<AlertDispatchRequested> PendingDispatchRequests => pendingDispatchRequests;

    public IReadOnlyCollection<AlertRecipientSelection> CurrentRecipients
        => recipientSelections.Where(recipient => recipient.AlertVersion == DraftVersion).ToArray();

    public bool HasReusableApprovalForCurrentVersion
        => ConfirmedDraftVersion == DraftVersion && State is AlertState.DispatchQueued or AlertState.Active;

    public static Alert CreateDraft(
        AlertId id,
        OrganizationId organizationId,
        SiteId siteId,
        DepartmentId departmentId,
        UserId createdByUserId,
        string simulationPatientReference,
        string location,
        string urgencyLabel,
        AlertSourceType sourceType,
        ProtectedValue? originalSource,
        DateTimeOffset createdAtUtc,
        ProtectedValue? structuredSuggestion = null)
    {
        return new Alert(
            id,
            organizationId,
            siteId,
            departmentId,
            createdByUserId,
            SimulationEnvironmentPolicy.RequireSyntheticPatientReference(simulationPatientReference),
            location.Trim(),
            urgencyLabel.Trim(),
            sourceType,
            originalSource,
            structuredSuggestion,
            UtcInstant.Require(createdAtUtc, nameof(createdAtUtc)));
    }

    public void UpdateSource(ProtectedValue originalSource, AlertDraftVersion expectedVersion, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(originalSource);
        EnsureEditable(expectedVersion);
        OriginalSource = originalSource;
        InvalidateApprovalAndIncrementVersion(updatedAtUtc, "source-edited", carryRecipients: true);
    }

    public void UpdateTypedContent(
        string location,
        string urgencyLabel,
        ProtectedValue originalSource,
        ProtectedValue structuredSuggestion,
        AlertDraftVersion expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentException.ThrowIfNullOrWhiteSpace(urgencyLabel);
        ArgumentNullException.ThrowIfNull(originalSource);
        ArgumentNullException.ThrowIfNull(structuredSuggestion);
        EnsureEditable(expectedVersion);
        Location = location.Trim();
        UrgencyLabel = urgencyLabel.Trim();
        OriginalSource = originalSource;
        StructuredSuggestion = structuredSuggestion;
        InvalidateApprovalAndIncrementVersion(updatedAtUtc, "typed-content-edited", carryRecipients: true);
    }

    public void SetStructuredSuggestion(
        ProtectedValue structuredSuggestion,
        AlertDraftVersion expectedVersion,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(structuredSuggestion);
        EnsureEditable(expectedVersion);
        StructuredSuggestion = structuredSuggestion;
        InvalidateApprovalAndIncrementVersion(updatedAtUtc, "sbar-edited", carryRecipients: true);
    }

    public void SetApprovedMessage(ProtectedValue approvedMessage, AlertDraftVersion expectedVersion, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(approvedMessage);
        EnsureEditable(expectedVersion);
        ApprovedMessage = approvedMessage;
        InvalidateApprovalAndIncrementVersion(updatedAtUtc, "approved-message-edited", carryRecipients: true);
    }

    public void SubmitForConfirmation(UserId actorUserId, AlertDraftVersion expectedVersion, DateTimeOffset occurredAtUtc)
    {
        EnsureExpectedVersion(expectedVersion);
        if (SourceType != AlertSourceType.Typed
            || OriginalSource is null
            || StructuredSuggestion is null
            || string.IsNullOrWhiteSpace(Location)
            || string.IsNullOrWhiteSpace(UrgencyLabel))
        {
            throw new DomainException("Alert drafts require a typed source, location, urgency, and structured SBAR content before submission.");
        }

        if (CurrentFieldConfirmations.Any(confirmation => confirmation.Status != FieldConfirmationStatus.Confirmed))
        {
            throw new UnresolvedCriticalFieldException("Every critical number and unit must be confirmed before submission.");
        }

        TransitionTo(AlertState.PendingConfirmation, actorUserId, "submitted-for-confirmation", "DEMO", occurredAtUtc);
    }

    public void ConfirmCriticalField(
        string fieldId,
        string originalValue,
        string normalizedValue,
        string? unit,
        UserId confirmedByUserId,
        AlertDraftVersion expectedVersion,
        DateTimeOffset confirmedAtUtc)
    {
        EnsureEditable(expectedVersion);
        if (string.IsNullOrWhiteSpace(fieldId) || string.IsNullOrWhiteSpace(originalValue) || string.IsNullOrWhiteSpace(normalizedValue))
        {
            throw new DomainException("Critical field confirmation requires an identifier and values.");
        }

        var canonicalFieldId = fieldId.Trim();
        var canonicalOriginalValue = originalValue.Trim();
        var canonicalNormalizedValue = normalizedValue.Trim();
        var canonicalUnit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        var existing = fieldConfirmations.SingleOrDefault(confirmation =>
            confirmation.FieldId == canonicalFieldId && confirmation.AlertVersion == DraftVersion);
        if (existing is null)
        {
            throw new DomainException("Critical field confirmation requires a field recorded for the current draft version.");
        }

        if (!string.Equals(existing.OriginalValue, canonicalOriginalValue, StringComparison.Ordinal)
            || !string.Equals(existing.Unit, canonicalUnit, StringComparison.Ordinal))
        {
            throw new DomainException("Critical field confirmation must match the recorded value and unit for the current draft version.");
        }

        if (existing.Status == FieldConfirmationStatus.Confirmed)
        {
            if (!string.Equals(existing.NormalizedValue, canonicalNormalizedValue, StringComparison.Ordinal))
            {
                throw new DomainException("A confirmed critical field cannot be rewritten within the same draft version.");
            }

            return;
        }

        var occurredAt = UtcInstant.Require(confirmedAtUtc, nameof(confirmedAtUtc));
        existing.Confirm(canonicalNormalizedValue, confirmedByUserId, occurredAt);
        UpdatedAtUtc = occurredAt;
    }

    public void RegisterUnresolvedCriticalField(string fieldId, string originalValue, string? unit, AlertDraftVersion expectedVersion)
    {
        EnsureEditable(expectedVersion);
        if (string.IsNullOrWhiteSpace(fieldId))
        {
            throw new DomainException("Unresolved critical fields require an identifier.");
        }

        UpsertCanonicalFieldConfirmation(
            fieldId.Trim(),
            originalValue.Trim(),
            originalValue.Trim(),
            string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
            FieldConfirmationStatus.Unresolved,
            CreatedByUserId,
            CreatedAtUtc);
    }

    public void ReplaceRecipients(
        IReadOnlyCollection<ValidatedRecipientSelection> recipients,
        UserId selectedByUserId,
        AlertDraftVersion expectedVersion,
        DateTimeOffset selectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        EnsureEditable(expectedVersion);
        var selectedAt = UtcInstant.Require(selectedAtUtc, nameof(selectedAtUtc));
        var validated = recipients.ToArray();
        var pairs = new HashSet<(PractitionerId PractitionerId, NotificationChannel Channel)>();
        foreach (var recipient in validated)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            AlertRecipientSelection.ValidateDirectoryRevision(recipient.DirectoryRevision);
            AlertRecipientSelection.ValidateOnCallSnapshot(recipient.OnCallSnapshot);
            if (recipient.DirectorySourceUpdatedAtUtc is not null)
            {
                UtcInstant.Require(recipient.DirectorySourceUpdatedAtUtc.Value, nameof(recipient.DirectorySourceUpdatedAtUtc));
            }

            if (!pairs.Add((recipient.PractitionerId, recipient.Channel)))
            {
                throw new DuplicateRecipientException("The practitioner is already selected for this channel.");
            }
        }

        InvalidateApprovalAndIncrementVersion(selectedAt, "recipients-replaced", carryRecipients: false);
        foreach (var recipient in validated)
        {
            recipientSelections.Add(new AlertRecipientSelection(
                AlertRecipientSelectionId.New(),
                OrganizationId,
                Id,
                DraftVersion,
                recipient.PractitionerId,
                recipient.PractitionerRoleId,
                recipient.Channel,
                selectedByUserId,
                selectedAt,
                recipient.DirectoryRevision,
                recipient.DirectorySourceUpdatedAtUtc,
                recipient.OnCallSnapshot));
        }
    }

    public void ConfirmForDispatch(
        UserId confirmingUserId,
        AlertDraftVersion expectedVersion,
        IReadOnlyCollection<Practitioner> currentPractitioners,
        DateTimeOffset confirmedAtUtc,
        string correlationId)
    {
        EnsureExpectedVersion(expectedVersion);
        if (State != AlertState.PendingConfirmation)
        {
            throw new InvalidAlertTransitionException("Dispatch confirmation is allowed only from PendingConfirmation.");
        }

        if (CurrentRecipients.Count == 0)
        {
            throw new RecipientsRequiredException("Confirmation requires at least one manually selected recipient.");
        }

        if (ApprovedMessage is null)
        {
            throw new DomainException("Confirmation requires an approved message.");
        }

        if (CurrentFieldConfirmations.Any(confirmation => confirmation.Status != FieldConfirmationStatus.Confirmed))
        {
            throw new UnresolvedCriticalFieldException("Every critical number and unit for this version must be confirmed.");
        }

        foreach (var recipient in CurrentRecipients)
        {
            var practitioner = currentPractitioners.SingleOrDefault(candidate => candidate.Id == recipient.PractitionerId)
                ?? throw new DomainException("Confirmation requires the current practitioner records for every recipient.");
            if (practitioner.OrganizationId != OrganizationId)
            {
                throw new OrganizationIsolationException("Confirmed recipients must remain in the alert organization.");
            }

            if (!practitioner.IsActive)
            {
                throw new InactivePractitionerException("Inactive practitioners cannot be confirmed for dispatch.");
            }
        }

        ConfirmedDraftVersion = DraftVersion;
        ConfirmedByUserId = confirmingUserId;
        ConfirmedAtUtc = UtcInstant.Require(confirmedAtUtc, nameof(confirmedAtUtc));
        pendingDispatchRequests.Add(new AlertDispatchRequested(Id, OrganizationId, DraftVersion));
        TransitionTo(AlertState.DispatchQueued, confirmingUserId, "human-confirmed", "DEMO", confirmedAtUtc, correlationId);
    }

    public void MarkActive(DateTimeOffset occurredAtUtc, string correlationId)
        => TransitionTo(AlertState.Active, actorUserId: null, "dispatch-activity", DemoEscalationPolicyVersion, occurredAtUtc, correlationId);

    public void MarkFailed(DateTimeOffset occurredAtUtc, string correlationId)
        => TransitionTo(AlertState.Failed, actorUserId: null, "durable-failure", DemoEscalationPolicyVersion, occurredAtUtc, correlationId);

    public void RetryFromFailure(UserId actorUserId, DateTimeOffset occurredAtUtc, string correlationId)
        => TransitionTo(AlertState.Active, actorUserId, "human-approved-retry", DemoEscalationPolicyVersion, occurredAtUtc, correlationId);

    public void Resolve(UserId actorUserId, DateTimeOffset occurredAtUtc, string correlationId)
    {
        TransitionTo(AlertState.Resolved, actorUserId, "human-resolved", DemoEscalationPolicyVersion, occurredAtUtc, correlationId);
        ResolvedByUserId = actorUserId;
        ResolvedAtUtc = UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc));
    }

    public void Cancel(UserId actorUserId, DateTimeOffset occurredAtUtc, string correlationId)
        => TransitionTo(AlertState.Cancelled, actorUserId, "human-cancelled", DemoEscalationPolicyVersion, occurredAtUtc, correlationId);

    public void ClearPendingDispatchRequests() => pendingDispatchRequests.Clear();

    private IReadOnlyCollection<AlertFieldConfirmation> CurrentFieldConfirmations
        => fieldConfirmations.Where(confirmation => confirmation.AlertVersion == DraftVersion).ToArray();

    private void UpsertCanonicalFieldConfirmation(
        string fieldId,
        string originalValue,
        string normalizedValue,
        string? unit,
        FieldConfirmationStatus status,
        UserId actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        var existing = fieldConfirmations.SingleOrDefault(confirmation =>
            confirmation.FieldId == fieldId && confirmation.AlertVersion == DraftVersion);
        if (existing is null)
        {
            fieldConfirmations.Add(new AlertFieldConfirmation(
                AlertFieldConfirmationId.New(),
                OrganizationId,
                Id,
                DraftVersion,
                fieldId,
                originalValue,
                normalizedValue,
                unit,
                status,
                actorUserId,
                occurredAtUtc));
            return;
        }

        existing.ReplaceCanonical(originalValue, normalizedValue, unit, status, actorUserId, occurredAtUtc);
    }

    private void EnsureEditable(AlertDraftVersion expectedVersion)
    {
        EnsureExpectedVersion(expectedVersion);
        if (State is not AlertState.Draft and not AlertState.PendingConfirmation)
        {
            throw new InvalidAlertTransitionException("Only Draft and PendingConfirmation alerts can be edited.");
        }
    }

    private void EnsureExpectedVersion(AlertDraftVersion expectedVersion)
    {
        if (expectedVersion != DraftVersion)
        {
            throw new StaleAlertVersionException("The supplied alert version is not the current draft version.");
        }
    }

    private void InvalidateApprovalAndIncrementVersion(DateTimeOffset updatedAtUtc, string reasonCode, bool carryRecipients)
    {
        var occurredAt = UtcInstant.Require(updatedAtUtc, nameof(updatedAtUtc));
        var previousRecipients = carryRecipients ? CurrentRecipients.ToArray() : [];
        var previousFields = CurrentFieldConfirmations.ToArray();
        DraftVersion = DraftVersion.Next();
        ConfirmedDraftVersion = null;
        ConfirmedByUserId = null;
        ConfirmedAtUtc = null;
        pendingDispatchRequests.Clear();
        UpdatedAtUtc = occurredAt;
        foreach (var field in previousFields)
        {
            fieldConfirmations.Add(new AlertFieldConfirmation(
                AlertFieldConfirmationId.New(),
                OrganizationId,
                Id,
                DraftVersion,
                field.FieldId,
                field.OriginalValue,
                field.NormalizedValue,
                field.Unit,
                FieldConfirmationStatus.Unresolved,
                CreatedByUserId,
                CreatedAtUtc));
        }

        foreach (var recipient in previousRecipients)
        {
            recipientSelections.Add(new AlertRecipientSelection(
                AlertRecipientSelectionId.New(),
                OrganizationId,
                Id,
                DraftVersion,
                recipient.PractitionerId,
                recipient.PractitionerRoleId,
                recipient.Channel,
                recipient.SelectedByUserId,
                recipient.SelectedAtUtc,
                recipient.DirectoryRevision,
                recipient.DirectorySourceUpdatedAtUtc,
                recipient.OnCallSnapshot));
        }

        if (State == AlertState.PendingConfirmation)
        {
            TransitionTo(AlertState.Draft, CreatedByUserId, reasonCode, "DEMO", occurredAt);
        }
    }

    private void TransitionTo(
        AlertState toState,
        UserId? actorUserId,
        string reasonCode,
        string policyVersion,
        DateTimeOffset occurredAtUtc,
        string correlationId = "alert-transition")
    {
        if (!AlertStateMachine.CanTransition(State, toState))
        {
            throw new InvalidAlertTransitionException($"Transition from {State} to {toState} is not allowed.");
        }

        var fromState = State;
        State = toState;
        UpdatedAtUtc = UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc));
        RecordTransition(fromState, toState, actorUserId, reasonCode, correlationId, policyVersion, occurredAtUtc);
    }

    private void RecordTransition(
        AlertState fromState,
        AlertState toState,
        UserId? actorUserId,
        string reasonCode,
        string correlationId,
        string policyVersion,
        DateTimeOffset occurredAtUtc)
    {
        stateTransitions.Add(new AlertStateTransition(
            AlertStateTransitionId.New(),
            OrganizationId,
            Id,
            fromState,
            toState,
            actorUserId,
            reasonCode,
            correlationId,
            policyVersion,
            UtcInstant.Require(occurredAtUtc, nameof(occurredAtUtc))));
    }
}
