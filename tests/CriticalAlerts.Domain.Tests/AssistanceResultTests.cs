using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AssistanceResultTests
{
    [Theory]
    [InlineData(AssistanceKind.Transcription)]
    public void ResultRequiresItsOwnProtectionPurpose(AssistanceKind kind)
    {
        var create = () => Create(kind, new([1], "test", ProtectedValuePurposes.AlertTypedSource), DateTimeOffset.UtcNow);
        create.Should().Throw<DomainException>();
    }
    private static AssistanceResult Create(AssistanceKind kind, ProtectedValue payload, DateTimeOffset when) =>
        AssistanceResult.Create(Guid.NewGuid(), OrganizationId.New(), AlertId.New(), new(3), AlertSourceRevisionId.New(), UserId.New(),
            kind, "Simulated", "DEMO-1", "DEMO-1", payload, when);
}
