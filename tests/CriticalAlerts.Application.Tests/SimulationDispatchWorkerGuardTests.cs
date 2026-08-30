using CriticalAlerts.Application.Dispatch;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class SimulationDispatchWorkerGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void SimulationWorkerMayBeEnabledOnlyInSimulationEnvironments(string environment)
    {
        var act = () => SimulationDispatchEnvironmentGuard.EnsureAllowed(environment, enabled: true);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    public void SimulationWorkerFailsClosedOutsideSimulationEnvironments(string environment)
    {
        var act = () => SimulationDispatchEnvironmentGuard.EnsureAllowed(environment, enabled: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void DisabledSimulationWorkerIsSafeOutsideSimulationEnvironments(string environment)
    {
        var act = () => SimulationDispatchEnvironmentGuard.EnsureAllowed(environment, enabled: false);

        act.Should().NotThrow();
    }
}
