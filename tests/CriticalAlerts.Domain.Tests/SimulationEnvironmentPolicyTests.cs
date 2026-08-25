using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Simulation;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class SimulationEnvironmentPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-19T16:00:00Z");

    [Fact]
    public void SiteSimulationCodeRequiresSyntheticPrefix()
    {
        var act = () => Site.Create(SiteId.New(), OrganizationId.New(), "North Wing Simulation Site", "NORTH", Now);

        act.Should().Throw<DomainException>().WithMessage("*SimulationEnvironmentPolicy*");
    }

    [Fact]
    public void DepartmentSimulationCodeRequiresSyntheticPrefix()
    {
        var act = () => Department.Create(
            DepartmentId.New(),
            OrganizationId.New(),
            SiteId.New(),
            "Fictional Emergency Care",
            "EMERGENCY",
            Now);

        act.Should().Throw<DomainException>().WithMessage("*SimulationEnvironmentPolicy*");
    }

    [Fact]
    public void HasSyntheticPrefixDoesNotInventProductionIdentifierRules()
    {
        SimulationEnvironmentPolicy.HasSyntheticPrefix("SIM-SITE-NORTH").Should().BeTrue();
        SimulationEnvironmentPolicy.HasSyntheticPrefix("SITE-NORTH").Should().BeFalse();
    }
}
