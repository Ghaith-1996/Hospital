namespace CriticalAlerts.Domain.Escalation;

// These exact human-reviewed strings identify the only implemented simulation rules.
// Changing policy text cannot silently change the meaning of a confirmed plan.
public static class DemoEscalationSemantics
{
    public const string TriggerCondition = "DEMO: elapsed PostgreSQL UTC delay or one unconsumed Declined/Unavailable response; REQUIRES_HOSPITAL_DECISION.";
    public const string StopCondition = "DEMO: stop on active exact-version responsibility, human resolve or cancel; pause suspends activation; REQUIRES_HOSPITAL_DECISION.";

    public static bool IsSupported(string trigger, string stop)
        => string.Equals(trigger, TriggerCondition, StringComparison.Ordinal)
            && string.Equals(stop, StopCondition, StringComparison.Ordinal);

    public static bool IsSupported(EscalationPlanDefinition definition)
        => IsSupported(definition.TriggerCondition, definition.StopCondition);
}
