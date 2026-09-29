using CriticalAlerts.Domain.Simulation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class SimulationEnvironmentPolicyTests
{
    [Fact]
    public void HasSyntheticPrefixDoesNotInventProductionIdentifierRules()
    {
        SimulationEnvironmentPolicy.HasSyntheticPrefix("SIM-SITE-NORTH").Should().BeTrue();
        SimulationEnvironmentPolicy.HasSyntheticPrefix("SITE-NORTH").Should().BeFalse();
    }
}
