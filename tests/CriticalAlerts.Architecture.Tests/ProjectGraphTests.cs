using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Architecture.Tests;

public sealed class ProjectGraphTests
{
    [Fact]
    public void DomainProjectHasNoRuntimeOrInfrastructureReferences()
    {
        var project = LoadProject("src/backend/CriticalAlerts.Domain/CriticalAlerts.Domain.csproj");

        ProjectReferences(project).Should().BeEmpty();
        PackageReferences(project).Should().BeEmpty();
    }

    [Fact]
    public void ApplicationProjectReferencesDomainOnly()
    {
        var project = LoadProject("src/backend/CriticalAlerts.Application/CriticalAlerts.Application.csproj");

        ProjectReferences(project).Should().Equal("CriticalAlerts.Domain");
        PackageReferences(project).Should().BeEmpty();
    }

    [Fact]
    public void ProjectReferenceParsingNormalizesWindowsSeparators()
    {
        var project = new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    new XElement(
                        "ProjectReference",
                        new XAttribute("Include", @"..\CriticalAlerts.Domain\CriticalAlerts.Domain.csproj")))));

        ProjectReferences(project).Should().Equal("CriticalAlerts.Domain");
    }

    [Fact]
    public void InfrastructureDoesNotReferenceApiOrWorker()
    {
        var project = LoadProject("src/backend/CriticalAlerts.Infrastructure/CriticalAlerts.Infrastructure.csproj");
        var references = ProjectReferences(project);

        references.Should().Contain("CriticalAlerts.Domain");
        references.Should().Contain("CriticalAlerts.Application");
        references.Should().NotContain("CriticalAlerts.Api");
        references.Should().NotContain("CriticalAlerts.Worker");
    }

    [Fact]
    public void WebIsNotAProjectReference()
    {
        var webRoot = Path.Combine(RepositoryRoot(), "src", "web");

        Directory.Exists(webRoot).Should().BeTrue();
        Directory.EnumerateFiles(webRoot, "*.csproj", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.EnumerateFiles(webRoot, "*.sln", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private static XDocument LoadProject(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(path).Should().BeTrue($"the Phase 1 project file must exist: {relativePath}");
        return XDocument.Load(path);
    }

    private static IReadOnlyList<string> ProjectReferences(XDocument project)
    {
        return project.Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> PackageReferences(XDocument project)
    {
        return project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(name => name is not null)
            .Cast<string>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
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
