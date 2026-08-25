using CriticalAlerts.Application.Directory;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class CsvDirectoryParserTests
{
    [Fact]
    public void HarborviewFixtureParsesTwelvePractitionersWithoutMergingSimilarNames()
    {
        var parsed = CsvDirectoryParser.Parse(File.ReadAllText(FixturePath()));

        parsed.Errors.Should().BeEmpty();
        parsed.Practitioners.Should().HaveCount(12);
        parsed.Practitioners.Select(practitioner => practitioner.LastName)
            .Where(name => name == "Martin")
            .Should().HaveCount(2);
        parsed.Practitioners.Select(practitioner => practitioner.SimulationCode)
            .Should()
            .OnlyHaveUniqueItems()
            .And.OnlyContain(code => code.StartsWith("SIM-PRAC-", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleRowsWithTheSameSourceRecordIdGroupIntoOnePractitioner()
    {
        var parsed = CsvDirectoryParser.Parse(File.ReadAllText(FixturePath()));
        var maya = parsed.Practitioners.Single(practitioner => practitioner.SourceRecordId == "SIM-SRC-MAYA");
        var harper = parsed.Practitioners.Single(practitioner => practitioner.SourceRecordId == "SIM-SRC-HARPER");
        var avery = parsed.Practitioners.Single(practitioner => practitioner.SourceRecordId == "SIM-SRC-AVERY");
        var taylor = parsed.Practitioners.Single(practitioner => practitioner.SourceRecordId == "SIM-SRC-TAYLOR");

        maya.Endpoints.Should().HaveCount(2);
        maya.OnCallAssignments.Should().ContainSingle(assignment => assignment.Tier == Domain.OnCallTier.Primary);
        harper.Roles.Should().HaveCount(2);
        avery.Endpoints.Should().BeEmpty();
        taylor.IsActive.Should().BeFalse();
        taylor.IsStale.Should().BeTrue();
    }

    [Fact]
    public void DuplicateSimulationCodesAreRejectedInsteadOfMatchingByName()
    {
        var csv = """
            source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status
            SIM-SRC-ONE,Jules,Martin,SIM-PRAC-0103,Surgery,SIM-SITE-RIVERSIDE,SIM-DEPT-SURGERY,Surgeon,true,true,2026-08-01T12:00:00Z,current
            SIM-SRC-TWO,Jordan,Martin,SIM-PRAC-0103,Surgery,SIM-SITE-RIVERSIDE,SIM-DEPT-SURGERY,Surgeon,true,true,2026-08-01T12:00:00Z,current
            """;

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "duplicate-simulation-code");
    }

    [Fact]
    public void MissingHeadersAreRejected()
    {
        var parsed = CsvDirectoryParser.Parse("first_name,last_name\nMaya,Chen\n");

        parsed.Errors.Should().Contain(error => error.Code == "missing-header");
    }

    [Fact]
    public void NonSyntheticPhoneEndpointsAreRejected()
    {
        var nonSynthetic = string.Concat("+1 ", "416", " ", "010", " ", "0101");
        var csv = "source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,endpoint_kind,endpoint_value,endpoint_label,source_updated_at_utc,freshness_status"
            + Environment.NewLine
            + string.Join(",",
            [
                "SIM-SRC-MAYA",
                "Maya",
                "Chen",
                "SIM-PRAC-0101",
                "Emergency",
                "SIM-SITE-NORTH",
                "SIM-DEPT-EMERGENCY",
                "Emergency physician",
                "true",
                "true",
                "Sms",
                nonSynthetic,
                "SIM-SMS-0101",
                "2026-08-01T12:00:00Z",
                "current",
            ]);

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Errors.Should().Contain(error => error.Code == "non-synthetic-endpoint");
    }

    private static string FixturePath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "fixtures", "simulation", "directory-harborview.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate fixtures/simulation/directory-harborview.csv.");
    }
}
