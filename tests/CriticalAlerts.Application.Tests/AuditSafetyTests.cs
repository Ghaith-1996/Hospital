using System.Text.Json;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Reliability;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Application.Tests;

public sealed class AuditSafetyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-19T12:00:00Z");

    [Fact]
    public void DefaultPageIsBoundedAndFiltersHaveNoIdentityOrSortOverride()
    {
        var query = new AuditQuery();
        query.PageSize.Should().Be(50);
        query.Validate();
        typeof(AuditQuery).GetProperties().Select(p => p.Name).Should().BeEquivalentTo(
            "OccurredFromUtc", "OccurredToUtc", "Action", "Outcome", "ResourceType", "CorrelationId", "Cursor", "PageSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void InvalidPageSizeFailsSafely(int size)
        => Assert.Throws<AuditQueryValidationException>(() => new AuditQuery(PageSize: size).Validate())
            .Message.Should().Be("Audit query is invalid. Review the filters and pagination.");

    [Fact]
    public void DateRangeRequiresUtcAndIncreasingBounds()
    {
        Assert.Throws<AuditQueryValidationException>(() => new AuditQuery(OccurredFromUtc: Now.ToOffset(TimeSpan.FromHours(1))).Validate());
        Assert.Throws<AuditQueryValidationException>(() => new AuditQuery(OccurredFromUtc: Now, OccurredToUtc: Now).Validate());
        Assert.Throws<AuditQueryValidationException>(() => new AuditQuery(OccurredFromUtc: Now, OccurredToUtc: Now.AddDays(-1)).Validate());
        new AuditQuery(OccurredFromUtc: Now, OccurredToUtc: Now.AddDays(1), PageSize: 100).Validate();
    }

    [Fact]
    public void CursorRoundTripsOnlyTimestampAndEventIdentifier()
    {
        var id = Guid.NewGuid();
        var cursor = AuditCursor.Encode(Now, id);
        AuditCursor.Decode(cursor).Should().Be(new AuditCursor(Now, id));
        cursor.Length.Should().BeLessThan(100);
    }

    [Fact]
    public void MalformedCursorsFailWithoutReflectingInput()
    {
        foreach (var input in new[] { "!", new string('x', 200), "AAAA", Convert.ToBase64String(new byte[24]) })
        {
            var failure = Assert.Throws<AuditQueryValidationException>(() => AuditCursor.Decode(input));
            failure.Message.Should().Be("Audit query is invalid. Review the filters and pagination.");
        }
    }

    [Fact]
    public void OnlyOpaqueCorrelationIdentifiersAreAccepted()
    {
        AuditSafety.IsSafeCorrelationId(Guid.NewGuid().ToString("D")).Should().BeTrue();
        AuditSafety.IsSafeCorrelationId(Guid.NewGuid().ToString("N")).Should().BeTrue();
        foreach (var input in ProtectedValues.Append(new string('a', 97)).Append("clinical-free-text"))
            AuditSafety.IsSafeCorrelationId(input).Should().BeFalse();
    }

    [Fact]
    public void UnknownFilterValuesFailWithConstantError()
    {
        foreach (var value in ProtectedValues)
        {
            foreach (var query in new[] { new AuditQuery(Action: value), new AuditQuery(Outcome: value),
                new AuditQuery(ResourceType: value), new AuditQuery(CorrelationId: value) })
            {
                var error = Assert.Throws<AuditQueryValidationException>(query.Validate);
                error.Message.Should().Be("Audit query is invalid. Review the filters and pagination.");
            }
        }
        new AuditQuery(Action: "alert.confirmed", Outcome: "succeeded", ResourceType: "alert",
            CorrelationId: Guid.NewGuid().ToString("N")).Validate();
    }

    [Fact]
    public void MetadataAllowsOnlyTypedCountsBooleansAndFiniteVocabulary()
    {
        var result = AuditSafety.ProjectMetadata("""
            {"version":3,"recipientCount":2,"channel":"Sms","channels":["SecureMessage","Voice"],
             "responseType":"Accepted","simulationOnly":true,"pageSize":50,"resultCount":2,
             "filtersUsed":["action","occurredFromUtc"],"nested":{"channel":"Sms"},"unknown":1}
            """);
        result.Keys.Should().BeEquivalentTo("version", "recipientCount", "channel", "channels",
            "responseType", "simulationOnly", "pageSize", "resultCount", "filtersUsed");
        result["version"].GetInt32().Should().Be(3);
        result["channels"].GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void MetadataDoesNotTrustSafeLookingKeysOrPartialArrays()
    {
        foreach (var value in ProtectedValues)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                channel = value,
                channels = new[] { "Sms", value },
                reasonCode = value,
                policyVersion = value,
                failureCategory = value,
                patientReference = value,
                version = value,
                simulationOnly = value,
                filtersUsed = new[] { value }
            });
            AuditSafety.ProjectMetadata(metadata).Should().BeEmpty();
        }
        AuditSafety.ProjectMetadata("""{"version":-1,"retryCount":1000001,"resultCount":1.5,"channel":{},"channels":[["Sms"]]}""")
            .Should().BeEmpty();
    }

    [Fact]
    public void MalformedOversizedNestedAndDuplicateMetadataFailClosed()
    {
        foreach (var metadata in new[] { "{", "null", "[]", new string(' ', 9000),
            """{"version":1,"version":2}""", """{"x":{"x":{"x":{"x":{"x":{"x":1}}}}}}""" })
            AuditSafety.ProjectMetadata(metadata).Should().BeEmpty();
    }

    [Fact]
    public void FullProjectionOmitsUntrustedTechnicalStringsAndRawMetadata()
    {
        var sentinel = ProtectedValues[0];
        var row = AuditEvent.Record(AuditEventId.New(), OrganizationId.New(), sentinel, UserId.New(),
            sentinel, sentinel, Guid.NewGuid(), sentinel, sentinel,
            JsonSerializer.Serialize(new { channel = sentinel }), Now);
        var view = AuditSafety.Project(row);
        var serialized = JsonSerializer.Serialize(view);
        ProtectedValues.Any(serialized.Contains).Should().BeFalse("audit output cannot reflect protected values");
        view.Action.Should().Be("unknown");
        view.CorrelationId.Should().BeNull();
        view.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void SafeProjectionPreservesAuthorizedOpaqueActorAndResourceEvidence()
    {
        var id = Guid.NewGuid();
        var actor = UserId.New();
        var correlation = Guid.NewGuid().ToString("N");
        var row = AuditEvent.Record(AuditEventId.New(), OrganizationId.New(), "user", actor,
            "alert.confirmed", "alert", id, "succeeded", correlation, """{"version":2}""", Now);
        var view = AuditSafety.Project(row);
        view.ResourceId.Should().Be(id);
        view.ActorUserId.Should().Be(actor.Value);
        view.CorrelationId.Should().Be(correlation);
        view.OccurredAtUtc.Should().Be(Now);
    }

    [Theory]
    [InlineData("alert.draft.submitted", "succeeded")]
    [InlineData("alert.recipients.replaced", "succeeded")]
    [InlineData("escalation-dispatch-queued", "succeeded")]
    [InlineData("escalation-recipients-activated", "activated")]
    [InlineData("escalation-exhausted", "manual-fallback")]
    [InlineData("escalation-processing-failed", "ConfirmedRecipientUnavailable")]
    [InlineData("escalation-processing-failed", "ConfirmedPlanInvalid")]
    [InlineData("escalation-processing-failed", "ProcessingError")]
    public void RetainedPhaseNineProducerVocabularyRemainsReadable(string action, string outcome)
    {
        var row = AuditEvent.Record(AuditEventId.New(), OrganizationId.New(), "SimulationWorker", null,
            action, "EscalationRun", Guid.NewGuid(), outcome, Guid.NewGuid().ToString("N"), "{}", Now);
        var view = AuditSafety.Project(row);
        view.Action.Should().Be(action);
        view.Outcome.Should().Be(outcome);
    }

    private static readonly string[] ProtectedValues =
    [
        "SIM-PATIENT-PHASE10-SENTINEL", "SIM-SECRET-PHASE10-SENTINEL", "+1-555-PHASE10",
        "phase10@example.invalid", "SIM-APPROVED-MESSAGE-DO-NOT-LOG",
    ];
}
