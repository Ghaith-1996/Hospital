using System.Text;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Domain.Tests;

public sealed class AlertSourceRevisionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-30T14:00:00Z");

    [Fact]
    public void SourceEditsKeepTheOriginalAndAppendAnImmutableVersionedRevision()
    {
        var original = Protect("SIMULATION: original typed source");
        var revised = Protect("SIMULATION: revised typed source");
        var alert = Alert.CreateDraft(
            AlertId.New(),
            OrganizationId.New(),
            SiteId.New(),
            DepartmentId.New(),
            UserId.New(),
            "SIM-PAT-SOURCE-001",
            ProtectPatient("SIM-PAT-SOURCE-001"),
            "North Wing / Simulation Room 205",
            "Urgent",
            AlertSourceType.Typed,
            original,
            Now);

        alert.UpdateSource(revised, alert.DraftVersion, Now.AddMinutes(1));

        alert.OriginalSource.Should().BeEquivalentTo(original);
        alert.SourceRevisions.Should().HaveCount(2);
        alert.SourceRevisions.Single(item => item.AlertVersion == AlertDraftVersion.Initial)
            .Source.Should().BeEquivalentTo(original);
        alert.SourceRevisions.Single(item => item.AlertVersion == alert.DraftVersion)
            .Source.Should().BeEquivalentTo(revised);
    }

    [Fact]
    public void PatientReferenceIsProtectedBeforeItCanBelongToAnAlert()
    {
        var alert = Alert.CreateDraft(
            AlertId.New(),
            OrganizationId.New(),
            SiteId.New(),
            DepartmentId.New(),
            UserId.New(),
            "SIM-PAT-PROTECTED-001",
            ProtectPatient("SIM-PAT-PROTECTED-001"),
            "North Wing / Simulation Room 205",
            "Urgent",
            AlertSourceType.Typed,
            Protect("SIMULATION: source"),
            Now);

        alert.SimulationPatientReference.Purpose.Should().Be("alert-patient-reference");
        alert.SimulationPatientReference.Ciphertext.Should().NotBeEmpty();
    }

    private static ProtectedValue Protect(string text)
        => new(Encoding.UTF8.GetBytes(text), "test-v1", "alert-typed-source");

    private static ProtectedValue ProtectPatient(string text)
        => new(Encoding.UTF8.GetBytes(text), "test-v1", "alert-patient-reference");
}
