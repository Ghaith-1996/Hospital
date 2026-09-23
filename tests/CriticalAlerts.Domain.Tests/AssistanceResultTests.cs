using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Assistance;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AssistanceResultTests
{
    [Theory]
    [InlineData(AssistanceKind.Transcription)]
    [InlineData(AssistanceKind.Structuring)]
    public void ResultRequiresItsOwnProtectionPurpose(AssistanceKind kind)
    {
        var create = () => Create(kind, new([1], "test", ProtectedValuePurposes.AlertTypedSource), DateTimeOffset.UtcNow);
        create.Should().Throw<DomainException>();
        var value = Create(kind, new([1], "test", AssistanceResult.PurposeFor(kind)), DateTimeOffset.UtcNow);
        value.Kind.Should().Be(kind);
        value.AlertVersion.Value.Should().Be(3);
        value.SourceRevisionId.Value.Should().NotBeEmpty();
    }
    [Fact]
    public void EvidenceTimestampMustBeUtc()
    {
        var create = () => Create(AssistanceKind.Transcription, new([1], "test", AssistanceResult.PurposeFor(AssistanceKind.Transcription)),
            new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(1)));
        create.Should().Throw<DomainException>();
    }
    private static AssistanceResult Create(AssistanceKind kind, ProtectedValue payload, DateTimeOffset when) =>
        AssistanceResult.Create(Guid.NewGuid(), OrganizationId.New(), AlertId.New(), new(3), AlertSourceRevisionId.New(), UserId.New(),
            kind, "Simulated", "DEMO-1", "DEMO-1", payload, when);
}
