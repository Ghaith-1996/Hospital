namespace CriticalAlerts.Domain.Alerts;

/// <summary>Identifier-only dispatch intent captured during confirmation. Clinical bodies are never included.</summary>
public sealed class AlertDispatchRequested
{
    public AlertDispatchRequested(AlertId alertId, OrganizationId organizationId, AlertDraftVersion draftVersion)
    {
        AlertId = alertId;
        OrganizationId = organizationId;
        DraftVersion = draftVersion;
    }

    public AlertId AlertId { get; }

    public OrganizationId OrganizationId { get; }

    public AlertDraftVersion DraftVersion { get; }
}
