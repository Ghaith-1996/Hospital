using CriticalAlerts.Application.Escalation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class SimulationEscalationEnvironmentGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void SimulationEnvironmentsAllowExplicitEnablement(string environment)
        => SimulationEscalationEnvironmentGuard.EnsureAllowed(environment, true);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    [InlineData("")]
    public void OtherEnvironmentsFailClosed(string? environment)
    {
        var start = () => SimulationEscalationEnvironmentGuard.EnsureAllowed(environment, true);
        start.Should().Throw<InvalidOperationException>();
        SimulationEscalationEnvironmentGuard.EnsureAllowed(environment, false);
    }

    [Fact]
    public void DefaultsAreDisabledAndAllLoopBoundsAreValidated()
    {
        var options = new EscalationWorkerOptions();
        options.Enabled.Should().BeFalse();
        options.Validate();
        foreach (var invalid in new[] {
            new EscalationWorkerOptions { BatchSize = 0 }, new EscalationWorkerOptions { BatchSize = 101 },
            new EscalationWorkerOptions { PollIntervalMilliseconds = 0 }, new EscalationWorkerOptions { PollIntervalMilliseconds = 300001 },
            new EscalationWorkerOptions { LeaseDuration = TimeSpan.Zero }, new EscalationWorkerOptions { LeaseDuration = TimeSpan.FromHours(2) } })
        {
            var validate = () => invalid.Validate();
            validate.Should().Throw<InvalidOperationException>();
        }
    }
}
