using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class WorkerConfigurationTests
{
    [Fact]
    public void WorkerDoesNotRegisterAlertDispatchHandlers()
    {
        var workerProgram = Path.Combine(RepositoryRoot(), "src", "backend", "CriticalAlerts.Worker", "Program.cs");

        File.Exists(workerProgram).Should().BeTrue("the Phase 1 worker shell must be present");
        var source = File.ReadAllText(workerProgram);

        source.IndexOf("AlertDispatchHandler", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        source.IndexOf("OutboxMessage", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        source.IndexOf("NotificationDispatcher", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        source.IndexOf("EscalationEvaluator", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
        source.IndexOf("ProviderAdapter", StringComparison.OrdinalIgnoreCase).Should().Be(-1);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
