using System.Collections.Frozen;
using System.Diagnostics.Metrics;

namespace CriticalAlerts.Infrastructure.Observability;

/// <summary>Process-local counters. No exporter, identity dimensions, or arbitrary tags.</summary>
public sealed class PlatformMetrics : IDisposable
{
    public Meter Meter { get; } = new("CriticalAlerts.Platform", "1.0.0");
    private readonly FrozenDictionary<string, Counter<long>> counters;

    public PlatformMetrics()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alert.confirmed"] = "criticalalerts.alert.confirmations",
            ["dispatch.completed"] = "criticalalerts.outbox.processed",
            ["dispatch.failed"] = "criticalalerts.dispatch.failures",
            ["dispatch.retry-scheduled"] = "criticalalerts.dispatch.retries",
            ["dispatch.delivery-event"] = "criticalalerts.delivery.events",
            ["recipient.response.acknowledged"] = "criticalalerts.responses",
            ["recipient.response.accepted"] = "criticalalerts.responses",
            ["recipient.response.declined"] = "criticalalerts.responses",
            ["recipient.response.unavailable"] = "criticalalerts.responses",
            ["recipient.response.callunitrequested"] = "criticalalerts.responses",
            ["directory.import.applied"] = "criticalalerts.directory.imports",
            ["audit.read"] = "criticalalerts.audit.queries",
            ["escalation-recipients-activated"] = "criticalalerts.escalation.steps",
            ["escalation-stopped"] = "criticalalerts.escalation.stopped",
            ["escalation-exhausted"] = "criticalalerts.escalation.exhausted",
            ["escalation-processing-failed"] = "criticalalerts.escalation.failures",
        };
        var instruments = names.Values.Distinct().ToDictionary(name => name, name => Meter.CreateCounter<long>(name, "{event}",
            "Committed simulation audit operation count; process-local and best effort."));
        counters = names.ToFrozenDictionary(item => item.Key, item => instruments[item.Value], StringComparer.Ordinal);
    }

    public void Record(string action)
    {
        if (counters.TryGetValue(action, out var counter))
            counter.Add(1, new KeyValuePair<string, object?>("operation", action));
    }

    public void Dispose() => Meter.Dispose();
}
