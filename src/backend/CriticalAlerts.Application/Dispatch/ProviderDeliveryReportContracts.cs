namespace CriticalAlerts.Application.Dispatch;

/// <summary>
/// A validated, authenticated provider delivery report. It carries only opaque identifiers, a closed status
/// and a UTC time; provider phone numbers and free-text details are never modelled.
/// </summary>
public sealed record ProviderDeliveryReport(
    string ProviderEventId,
    string ProviderMessageId,
    string? Tag,
    ProviderDeliveryReportStatus Status,
    DateTimeOffset OccurredAtUtc);

public enum ProviderDeliveryReportStatus
{
    Delivered,
    Failed,
}

public enum ProviderDeliveryReportOutcome
{
    Applied,
    NoStateChange,
    Duplicate,
    Unmatched,
    TagMismatch,
}

public interface IProviderDeliveryReportService
{
    Task<ProviderDeliveryReportOutcome> ApplyAsync(
        string provider,
        ProviderDeliveryReport report,
        string correlationId,
        CancellationToken cancellationToken);
}
