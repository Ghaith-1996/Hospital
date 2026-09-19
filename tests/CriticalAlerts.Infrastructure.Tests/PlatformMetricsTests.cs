using System.Diagnostics.Metrics;
using CriticalAlerts.Infrastructure.Observability;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class PlatformMetricsTests
{
    [Theory]
    [InlineData("alert.confirmed", "criticalalerts.alert.confirmations")]
    [InlineData("dispatch.completed", "criticalalerts.outbox.processed")]
    [InlineData("dispatch.failed", "criticalalerts.dispatch.failures")]
    [InlineData("dispatch.retry-scheduled", "criticalalerts.dispatch.retries")]
    [InlineData("dispatch.delivery-event", "criticalalerts.delivery.events")]
    [InlineData("recipient.response.accepted", "criticalalerts.responses")]
    [InlineData("directory.import.applied", "criticalalerts.directory.imports")]
    [InlineData("audit.read", "criticalalerts.audit.queries")]
    [InlineData("escalation-recipients-activated", "criticalalerts.escalation.steps")]
    [InlineData("escalation-stopped", "criticalalerts.escalation.stopped")]
    [InlineData("escalation-exhausted", "criticalalerts.escalation.exhausted")]
    [InlineData("escalation-processing-failed", "criticalalerts.escalation.failures")]
    public void ExpectedMeasurementsUseOnlyFiniteOperationTags(string action, string expected)
    {
        using var metrics = new PlatformMetrics();
        var values = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, observer) =>
        {
            if (ReferenceEquals(instrument.Meter, metrics.Meter)) observer.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => values.Add((instrument.Name, value, tags.ToArray())));
        listener.Start();
        metrics.Record(action);
        values.Should().ContainSingle();
        values[0].Name.Should().Be(expected);
        values[0].Value.Should().Be(1);
        values[0].Tags.Should().ContainSingle().Which.Key.Should().Be("operation");
        values[0].Tags[0].Value.Should().Be(action);
        metrics.Record("SIM-PATIENT-PHASE10-SENTINEL");
        metrics.Record("phase10@example.invalid");
        metrics.Record(Guid.NewGuid().ToString());
        values.Should().ContainSingle();
    }
}
