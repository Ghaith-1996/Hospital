using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Architecture.Tests;

public sealed class RepositorySafetyTests
{
    [Fact]
    public void NoTrackedEnvFileExists()
    {
        Files().Where(file => string.Equals(Path.GetFileName(file), ".env", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }

    [Fact]
    public void NoApplicationProjectContainsPhase2Migration()
    {
        var backendRoot = Path.Combine(RepositoryRoot(), "src", "backend");
        Directory.EnumerateFiles(backendRoot, "*Migration*.cs", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.EnumerateDirectories(backendRoot, "Migrations", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void NoFixtureContainsNonSyntheticPhonePattern()
    {
        var phonePattern = new Regex(
            @"(?<!\d)(?:\+?1[\s.-]?)?\(?([2-9]\d{2})\)?[\s.-]\d{3}[\s.-]\d{4}(?!\d)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var file in Files().Where(IsTestOrFixture))
        {
            foreach (Match match in phonePattern.Matches(File.ReadAllText(file)))
            {
                match.Groups[1].Value.Should().Be("555", $"only fictional 555 numbers are allowed in {file}");
            }
        }
    }

    [Fact]
    public void NoProviderCredentialPatternExists()
    {
        var credentialPattern = new Regex(
            @"-----BEGIN (?:RSA|EC|OPENSSH|PRIVATE) KEY-----|\b(?:AKIA|ASIA)[0-9A-Z]{16}\b|\b(?:ghp|github_pat|glpat|sk-)[A-Za-z0-9_-]{20,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        foreach (var file in Files().Where(file => !file.EndsWith(".env.example", StringComparison.OrdinalIgnoreCase)))
        {
            credentialPattern.IsMatch(File.ReadAllText(file)).Should().BeFalse($"credential material must not be present in {file}");
        }
    }

    private static bool IsTestOrFixture(string path)
    {
        return path.Contains("tests", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Contains("Fixture", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Files()
    {
        var ignored = new[] { ".git", ".next", "node_modules", "bin", "obj", "TestResults", "playwright-report", "test-results", ".playwright-browsers", ".dotnet" };
        return Directory.EnumerateFiles(RepositoryRoot(), "*", SearchOption.AllDirectories)
            .Where(file => (file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Intersect(ignored, StringComparer.OrdinalIgnoreCase)).Count() == 0);
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
