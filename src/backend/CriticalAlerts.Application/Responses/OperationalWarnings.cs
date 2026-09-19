namespace CriticalAlerts.Application.Responses;

public sealed record OperationalWarning(string Code, string Title, string Explanation,
    string RecommendedApplicationAction, bool RequiresHospitalFallback);

public sealed record OperationalWarningFacts(bool ProviderUnavailable = false, bool DispatchDelayed = false,
    bool DeliveryFailed = false, bool DirectoryStale = false, bool DirectorySynchronizationFailed = false,
    bool DatabaseUnavailable = false, bool EscalationProcessingDelayed = false, bool EscalationExhausted = false);

public static class OperationalWarnings
{
    public static IReadOnlyList<OperationalWarning> From(OperationalWarningFacts facts)
    {
        var warnings = new List<OperationalWarning>();
        if (facts.ProviderUnavailable) warnings.Add(new("ProviderUnavailable", "Provider unavailable",
            "The simulated notification provider is unavailable. The alert remains recorded.",
            "Refresh status. Do not create a duplicate alert. Follow the approved manual fallback procedure: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.DispatchDelayed) warnings.Add(new("DispatchDelayed", "Dispatch delayed",
            "Notification processing is delayed. The confirmed alert remains durable.",
            "Refresh status before retrying. If delay persists, follow the approved manual fallback procedure: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.DeliveryFailed) warnings.Add(new("DeliveryFailed", "Delivery failed",
            "A recorded notification attempt failed. Delivery does not establish responsibility.",
            "Review attempts and refresh status. Do not create a duplicate alert. Manual fallback: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.DirectoryStale) warnings.Add(new("DirectoryStale", "Directory stale",
            "Selected directory information is marked stale.",
            "Review directory freshness and source evidence before taking another recipient action.", false));
        if (facts.DirectorySynchronizationFailed) warnings.Add(new("DirectorySynchronizationFailed", "Directory synchronization failed",
            "The latest recorded directory synchronization did not succeed.",
            "Review the latest safe synchronization status and validate a new import. Directory fallback: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.DatabaseUnavailable) warnings.Add(new("DatabaseUnavailable", "Database unavailable",
            "Dependency-dependent operations are unavailable.",
            "Check readiness and refresh status before retrying. Recovery authority: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.EscalationProcessingDelayed) warnings.Add(new("EscalationProcessingDelayed", "Escalation processing delayed",
            "A confirmed automatic step is overdue or its processing failed.",
            "Refresh status and review the confirmed plan. Do not manually edit workflow state. Manual fallback: REQUIRES_HOSPITAL_DECISION.", true));
        if (facts.EscalationExhausted) warnings.Add(new("EscalationExhausted", "Escalation steps exhausted",
            "All approved automatic steps have been queued. Delivery and responsibility remain separate.",
            "Review attempts and responsibility status. If responsibility remains unassigned, use the approved fallback: REQUIRES_HOSPITAL_DECISION.", true));
        return warnings;
    }
}
