using CriticalAlerts.Application.Responses;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class SimulationResponseEnvironmentGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void EnabledResponsesAreAllowedOnlyInSimulationEnvironments(string environment)
    {
        var act = () => SimulationResponseEnvironmentGuard.EnsureAllowed(environment, enabled: true);

        act.Should().NotThrow();
        SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environment).Should().BeTrue();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    public void EnabledResponsesFailClosedOutsideSimulationEnvironments(string environment)
    {
        var act = () => SimulationResponseEnvironmentGuard.EnsureAllowed(environment, enabled: true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
        SimulationResponseEnvironmentGuard.IsSimulationEnvironment(environment).Should().BeFalse();
    }
}
