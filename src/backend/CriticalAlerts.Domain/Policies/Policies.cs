namespace CriticalAlerts.Domain.Policies;

public sealed class AlertTemplate
{
    private AlertTemplate()
    {
        Name = string.Empty;
        Version = string.Empty;
        SchemaJson = string.Empty;
    }

    private AlertTemplate(
        AlertTemplateId id,
        OrganizationId organizationId,
        string name,
        string version,
        bool isActive,
        string schemaJson)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Version = version;
        IsActive = isActive;
        SchemaJson = schemaJson;
    }

    public AlertTemplateId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string Version { get; private set; }

    public bool IsActive { get; private set; }

    public string SchemaJson { get; private set; }

    public static AlertTemplate CreateDemo(AlertTemplateId id, OrganizationId organizationId, DateTimeOffset createdAtUtc)
    {
        _ = UtcInstant.Require(createdAtUtc, nameof(createdAtUtc));
        return new AlertTemplate(
            id,
            organizationId,
            "DEMO urgent specialist consultation",
            "DEMO-1",
            isActive: true,
            """{"requiredFields":["source","urgency","patientReference","location"],"numericConfirmationFields":["REQUIRES_HOSPITAL_DECISION"]}""");
    }
}

public sealed class NotificationPolicy
{
    private NotificationPolicy()
    {
        Version = string.Empty;
        AllowedChannels = string.Empty;
        GenericSmsTemplate = string.Empty;
        GenericVoiceTemplate = string.Empty;
    }

    private NotificationPolicy(
        NotificationPolicyId id,
        OrganizationId organizationId,
        string version,
        bool isActive,
        string allowedChannels,
        string genericSmsTemplate,
        string genericVoiceTemplate,
        int retryLimit)
    {
        Id = id;
        OrganizationId = organizationId;
        Version = version;
        IsActive = isActive;
        AllowedChannels = allowedChannels;
        GenericSmsTemplate = genericSmsTemplate;
        GenericVoiceTemplate = genericVoiceTemplate;
        RetryLimit = retryLimit;
    }

    public NotificationPolicyId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Version { get; private set; }

    public bool IsActive { get; private set; }

    public string AllowedChannels { get; private set; }

    public string GenericSmsTemplate { get; private set; }

    public string GenericVoiceTemplate { get; private set; }

    public int RetryLimit { get; private set; }

    public static NotificationPolicy CreateDemo(NotificationPolicyId id, OrganizationId organizationId)
    {
        return new NotificationPolicy(
            id,
            organizationId,
            "DEMO-1",
            isActive: true,
            "SecureMessage,Sms,Voice",
            "SIMULATION: please open the secure alert application.",
            "SIMULATION: please open the secure alert application.",
            retryLimit: 1);
    }
}

public sealed class EscalationPolicy
{
    private EscalationPolicy()
    {
        Name = string.Empty;
        Version = string.Empty;
        TriggerCondition = string.Empty;
        StopCondition = string.Empty;
    }

    private EscalationPolicy(
        EscalationPolicyId id,
        OrganizationId organizationId,
        string name,
        string version,
        bool isActive,
        string triggerCondition,
        string stopCondition)
    {
        Id = id;
        OrganizationId = organizationId;
        Name = name;
        Version = version;
        IsActive = isActive;
        TriggerCondition = triggerCondition;
        StopCondition = stopCondition;
    }

    public EscalationPolicyId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string Name { get; private set; }

    public string Version { get; private set; }

    public bool IsActive { get; private set; }

    public string TriggerCondition { get; private set; }

    public string StopCondition { get; private set; }

    public static EscalationPolicy CreateDemo(EscalationPolicyId id, OrganizationId organizationId)
    {
        return new EscalationPolicy(
            id,
            organizationId,
            "DEMO sequential backup",
            "DEMO-1",
            isActive: true,
            "REQUIRES_HOSPITAL_DECISION: simulation uses a deterministic fake clock only.",
            "REQUIRES_HOSPITAL_DECISION: simulation stops only on explicit human resolve/cancel.");
    }
}

public sealed class EscalationStep
{
    private EscalationStep()
    {
        RecipientSource = string.Empty;
        Channels = string.Empty;
    }

    private EscalationStep(
        EscalationStepId id,
        OrganizationId organizationId,
        EscalationPolicyId policyId,
        int sequenceNumber,
        TimeSpan delay,
        string recipientSource,
        string channels,
        int maxAttempts)
    {
        Id = id;
        OrganizationId = organizationId;
        PolicyId = policyId;
        SequenceNumber = sequenceNumber;
        Delay = delay;
        RecipientSource = recipientSource;
        Channels = channels;
        MaxAttempts = maxAttempts;
    }

    public EscalationStepId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public EscalationPolicyId PolicyId { get; private set; }

    public int SequenceNumber { get; private set; }

    public TimeSpan Delay { get; private set; }

    public string RecipientSource { get; private set; }

    public string Channels { get; private set; }

    public int MaxAttempts { get; private set; }

    public static EscalationStep CreateDemo(
        EscalationStepId id,
        OrganizationId organizationId,
        EscalationPolicyId policyId,
        int sequenceNumber)
    {
        return new EscalationStep(
            id,
            organizationId,
            policyId,
            sequenceNumber,
            TimeSpan.FromMinutes(sequenceNumber),
            "DEMO backup on-call assignment",
            "SecureMessage",
            maxAttempts: 1);
    }
}
