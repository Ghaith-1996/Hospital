using CriticalAlerts.Application.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CriticalAlerts.Infrastructure.Observability;

/// <summary>The only application log category enabled at runtime. Never accepts payloads or exceptions.</summary>
public static partial class CriticalAlertsOperationalLog
{
    public const string Category = "CriticalAlerts.Operations";

    public static void Configure(ILoggingBuilder logging)
    {
        // Post-configuration prevents configuration-file rules from enabling framework request/SQL logs.
        logging.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.Rules.Add(new LoggerFilterRule(null, null, LogLevel.Information,
                (_, category, level) => category == Category && level >= LogLevel.Information));
        });
    }

    public static void Rejected(ILogger logger, int status, string correlationId) =>
        ApiRequestRejected(logger, Math.Clamp(status, 400, 599), SafeCorrelation(correlationId));

    public static void ReadinessFailed(ILogger logger, string correlationId) =>
        DatabaseReadinessFailed(logger, SafeCorrelation(correlationId));

    public static void WorkerState(ILogger logger, string operation, bool failed) =>
        WorkerOperationalState(logger, operation is "dispatch" or "escalation" ? operation : "platform",
            failed ? "retry-pending" : "started");

    public static void Completed(ILogger logger, string action, string correlationId) =>
        WorkflowOperationRecorded(logger, AuditSafety.Actions.Contains(action) ? action : "unknown", SafeCorrelation(correlationId));

    private static string SafeCorrelation(string? value) =>
        AuditSafety.IsSafeCorrelationId(value) ? value! : "unavailable";

    [LoggerMessage(1001, LogLevel.Warning, "ApiRequestRejected status={Status} correlationId={CorrelationId}")]
    private static partial void ApiRequestRejected(ILogger logger, int status, string correlationId);

    [LoggerMessage(1002, LogLevel.Warning, "DatabaseReadinessFailed category=database-unavailable correlationId={CorrelationId}")]
    private static partial void DatabaseReadinessFailed(ILogger logger, string correlationId);

    [LoggerMessage(1003, LogLevel.Information, "WorkerOperationalState operation={Operation} outcome={Outcome}")]
    private static partial void WorkerOperationalState(ILogger logger, string operation, string outcome);

    [LoggerMessage(1004, LogLevel.Information, "WorkflowOperationRecorded operation={Operation} correlationId={CorrelationId}")]
    private static partial void WorkflowOperationRecorded(ILogger logger, string operation, string correlationId);
}
