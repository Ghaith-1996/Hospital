namespace CriticalAlerts.Domain;

public readonly record struct OrganizationId(Guid Value)
{
    public static OrganizationId New() => new(Guid.NewGuid());
}

public readonly record struct SiteId(Guid Value)
{
    public static SiteId New() => new(Guid.NewGuid());
}

public readonly record struct DepartmentId(Guid Value)
{
    public static DepartmentId New() => new(Guid.NewGuid());
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());
}

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());
}

public readonly record struct ExternalIdentityId(Guid Value)
{
    public static ExternalIdentityId New() => new(Guid.NewGuid());
}

public readonly record struct PractitionerUserLinkId(Guid Value)
{
    public static PractitionerUserLinkId New() => new(Guid.NewGuid());
}

public readonly record struct PractitionerId(Guid Value)
{
    public static PractitionerId New() => new(Guid.NewGuid());
}

public readonly record struct PractitionerRoleId(Guid Value)
{
    public static PractitionerRoleId New() => new(Guid.NewGuid());
}

public readonly record struct ContactEndpointId(Guid Value)
{
    public static ContactEndpointId New() => new(Guid.NewGuid());
}

public readonly record struct OnCallAssignmentId(Guid Value)
{
    public static OnCallAssignmentId New() => new(Guid.NewGuid());
}

public readonly record struct DirectorySourceRecordId(Guid Value)
{
    public static DirectorySourceRecordId New() => new(Guid.NewGuid());
}

public readonly record struct DirectorySyncRunId(Guid Value)
{
    public static DirectorySyncRunId New() => new(Guid.NewGuid());
}

public readonly record struct AlertId(Guid Value)
{
    public static AlertId New() => new(Guid.NewGuid());
}

public readonly record struct AlertDraftVersion(int Value)
{
    public static AlertDraftVersion Initial { get; } = new(1);

    public AlertDraftVersion Next() => new(Value + 1);
}

public readonly record struct AlertFieldConfirmationId(Guid Value)
{
    public static AlertFieldConfirmationId New() => new(Guid.NewGuid());
}

public readonly record struct AlertRecipientSelectionId(Guid Value)
{
    public static AlertRecipientSelectionId New() => new(Guid.NewGuid());
}

public readonly record struct AlertStateTransitionId(Guid Value)
{
    public static AlertStateTransitionId New() => new(Guid.NewGuid());
}

public readonly record struct DeliveryAttemptId(Guid Value)
{
    public static DeliveryAttemptId New() => new(Guid.NewGuid());
}

public readonly record struct DeliveryEventId(Guid Value)
{
    public static DeliveryEventId New() => new(Guid.NewGuid());
}

public readonly record struct SimulationDispatchScenarioSettingId(Guid Value)
{
    public static SimulationDispatchScenarioSettingId New() => new(Guid.NewGuid());
}

public readonly record struct RecipientResponseId(Guid Value)
{
    public static RecipientResponseId New() => new(Guid.NewGuid());
}

public readonly record struct ResponsibilityAssignmentId(Guid Value)
{
    public static ResponsibilityAssignmentId New() => new(Guid.NewGuid());
}

public readonly record struct EscalationRunId(Guid Value)
{
    public static EscalationRunId New() => new(Guid.NewGuid());
}

public readonly record struct AlertTemplateId(Guid Value)
{
    public static AlertTemplateId New() => new(Guid.NewGuid());
}

public readonly record struct NotificationPolicyId(Guid Value)
{
    public static NotificationPolicyId New() => new(Guid.NewGuid());
}

public readonly record struct EscalationPolicyId(Guid Value)
{
    public static EscalationPolicyId New() => new(Guid.NewGuid());
}

public readonly record struct EscalationStepId(Guid Value)
{
    public static EscalationStepId New() => new(Guid.NewGuid());
}

public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.NewGuid());
}

public readonly record struct OutboxMessageId(Guid Value)
{
    public static OutboxMessageId New() => new(Guid.NewGuid());
}

public readonly record struct InboxMessageId(Guid Value)
{
    public static InboxMessageId New() => new(Guid.NewGuid());
}

public readonly record struct IdempotencyRecordId(Guid Value)
{
    public static IdempotencyRecordId New() => new(Guid.NewGuid());
}
