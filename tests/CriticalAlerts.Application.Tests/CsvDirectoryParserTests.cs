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
        parsed.Errors.Should().NotContain(error => error.Message.Contains(nonSynthetic, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateHeadersAreRejected()
    {
        var csv = "source_record_id,first_name,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status\n";

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "duplicate-header");
    }

    [Fact]
    public void RowsWithUnexpectedColumnCountsAreRejected()
    {
        var csv = "source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status\n"
            + "SIM-SRC-MAYA,Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-NORTH,SIM-DEPT-EMERGENCY,Emergency physician,true,true,2026-08-01T12:00:00Z,current,unexpected\n";

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "invalid-column-count");
    }

    [Fact]
    public void UnterminatedQuotedFieldIsRejected()
    {
        var csv = string.Join(',', CsvDirectoryParser.RequiredHeaders)
            + Environment.NewLine
            + "SIM-SRC-ONE,\"Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-NORTH,SIM-DEPT-EMERGENCY,Emergency physician,true,true,2026-08-01T12:00:00Z,current";

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "malformed-csv");
    }

    [Fact]
    public void SecureMessageEndpointsRequireTheSimulationScheme()
    {
        var parsed = CsvDirectoryParser.Parse(ValidSingleRow("SecureMessage", "https://example.invalid/endpoint"));

        parsed.Errors.Should().Contain(error => error.Code == "non-synthetic-endpoint");
    }

    [Fact]
    public void PhoneValidationRequiresACompleteSynthetic555Number()
    {
        var parsed = CsvDirectoryParser.Parse(ValidSingleRow("Sms", "+1 555-010-0101-extra"));

        parsed.Errors.Should().Contain(error => error.Code == "non-synthetic-endpoint");
    }

    [Fact]
    public void UnknownRoleTitlesAreRejected()
    {
        var parsed = CsvDirectoryParser.Parse(ValidSingleRow("Sms", "+1 555 010 0101", roleTitle: "Administrator"));

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "invalid-role");
    }

    [Fact]
    public void OnCallWindowsMustEndAfterTheyStart()
    {
        var parsed = CsvDirectoryParser.Parse(ValidOnCallRow(
            "2026-08-08T12:00:00Z",
            "2026-08-01T12:00:00Z"));

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "invalid-on-call-window");
    }

    [Fact]
    public void PractitionerIdentityMustComeFromTheFictionalSimulationCatalog()
    {
        var csv = ValidSingleRow("Sms", "+1 555 010 0101", firstName: "Unknown", lastName: "Person");

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "non-fictional-practitioner");
    }

    [Fact]
    public void SpecialtyMustComeFromTheFictionalSimulationCatalog()
    {
        var csv = ValidSingleRow("Sms", "+1 555 010 0101", specialty: "Unlisted specialty");

        var parsed = CsvDirectoryParser.Parse(csv);

        parsed.Practitioners.Should().BeEmpty();
        parsed.Errors.Should().Contain(error => error.Code == "non-fictional-specialty");
    }

    [Fact]
    public void ExactDuplicateRowsAreDeduplicatedInTheNormalizedRecord()
    {
        var single = ValidOnCallRow("2026-08-01T12:00:00Z", "2026-08-08T12:00:00Z");
        var lines = single.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var csv = lines[0] + Environment.NewLine + lines[1] + Environment.NewLine + lines[1] + Environment.NewLine;

        var parsed = CsvDirectoryParser.Parse(csv);
        var practitioner = parsed.Practitioners.Single();

        parsed.Errors.Should().BeEmpty();
        practitioner.Roles.Should().ContainSingle();
        practitioner.OnCallAssignments.Should().ContainSingle();
    }

    private static string ValidSingleRow(
        string endpointKind,
        string endpointValue,
        string roleTitle = "Emergency physician",
        string firstName = "Maya",
        string lastName = "Chen",
        string specialty = "Emergency")
        => "source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,endpoint_kind,endpoint_value,endpoint_label,source_updated_at_utc,freshness_status"
            + Environment.NewLine
            + string.Join(",",
            [
                "SIM-SRC-ONE", firstName, lastName, "SIM-PRAC-0101", specialty, "SIM-SITE-NORTH",
                "SIM-DEPT-EMERGENCY", roleTitle, "true", "true", endpointKind, endpointValue,
                "SIM-ENDPOINT-0101", "2026-08-01T12:00:00Z", "current",
            ])
            + Environment.NewLine;

    private static string ValidOnCallRow(string startsAtUtc, string endsAtUtc)
        => string.Join(",",
            [
                "source_record_id", "first_name", "last_name", "simulation_code", "specialty", "site_code", "department_code",
                "role_title", "is_primary_role", "is_active", "endpoint_kind", "endpoint_value", "endpoint_label", "on_call_tier",
                "on_call_starts_at_utc", "on_call_ends_at_utc", "source_updated_at_utc", "freshness_status",
            ])
            + Environment.NewLine
            + string.Join(",",
            [
                "SIM-SRC-ONCALL", "Maya", "Chen", "SIM-PRAC-0101", "Emergency", "SIM-SITE-NORTH", "SIM-DEPT-EMERGENCY",
                "Emergency physician", "true", "true", "", "", "", "Primary", startsAtUtc, endsAtUtc,
                "2026-08-01T12:00:00Z", "current",
            ])
            + Environment.NewLine;

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
