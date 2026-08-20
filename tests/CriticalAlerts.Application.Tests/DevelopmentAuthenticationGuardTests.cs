using CriticalAlerts.Application.Identity;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class DevelopmentAuthenticationGuardTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void EnabledIsAllowedInSimulationEnvironments(string environment)
    {
        var act = () => DevelopmentAuthenticationGuard.EnsureAllowed(environment, enabled: true);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("")]
    [InlineData(null)]
    public void EnabledIsRejectedOutsideSimulationEnvironments(string? environment)
    {
        var act = () => DevelopmentAuthenticationGuard.EnsureAllowed(environment, enabled: true);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be enabled outside Development or Test*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Development")]
    public void DisabledIsAllowedInAnyEnvironment(string environment)
    {
        var act = () => DevelopmentAuthenticationGuard.EnsureAllowed(environment, enabled: false);
        act.Should().NotThrow();
    }
}
