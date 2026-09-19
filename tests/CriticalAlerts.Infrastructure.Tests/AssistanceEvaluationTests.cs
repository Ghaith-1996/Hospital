using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Infrastructure.Tests;

public sealed class AssistanceEvaluationMetricTests
{
    [Fact]
    public void WordErrorCountsSubstitutionDeletionAndInsertion()
    {
        EvaluationMetrics.WordErrors("fictional value 8.2 mmol/L", "fictional value 82").Should().Be(2);
        EvaluationMetrics.WordErrors("no pain", "pain").Should().Be(1);
        EvaluationMetrics.WordErrors("no pain", "no fictional pain").Should().Be(1);
    }
    [Fact]
    public void NumbersAndUnitsAreExactAndMultiplicityMatters()
    {
        EvaluationMetrics.ExactMatches(["8.2|mmol/L", "82|mmHg"], ["82|mmol/L", "82|mmHg"]).Should().Be(1);
        EvaluationMetrics.ExactMatches(["82", "82"], ["82"]).Should().Be(1);
        EvaluationMetrics.ExactMatches(["8,2|mmol/L"], ["8.2|mmol/L"]).Should().Be(0);
    }
    [Fact]
    public void EmptyDenominatorIsUnavailableRatherThanPerfect()
    {
        EvaluationMetrics.Rate(0, 0).Should().BeNull();
        EvaluationMetrics.Rate(1, 2).Should().Be(0.5m);
    }
}
