using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class WorkerConfigurationTests
{
    [Fact]
    public void WorkerRegistersOnlyTheSimulationDispatchBoundary()
    {
        var workerProgram = Path.Combine(RepositoryRoot(), "src", "backend", "CriticalAlerts.Worker", "Program.cs");

        File.Exists(workerProgram).Should().BeTrue("the worker entry point must be present");
        var source = File.ReadAllText(workerProgram);

        source.Should().Contain("SimulationDispatchEnvironmentGuard");
        source.Should().Contain("AddSimulationDispatch");
        source.Should().Contain("SimulationDispatchWorker");
        source.Should().NotContain("HttpClient");
        source.Should().NotContain("Twilio");
        source.Should().NotContain("Azure.Communication");
        source.Should().NotContain("Vonage");
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
