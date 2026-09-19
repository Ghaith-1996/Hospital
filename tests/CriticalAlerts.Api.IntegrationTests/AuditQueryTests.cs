using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CriticalAlerts.Application.Audit;
using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AuditQueryTests(SeededPostgresApiFixture fixture)
{
    [Theory]
    [InlineData("Auditor", 200)]
    [InlineData("SystemAdministrator", 200)]
    [InlineData("Operator", 403)]
    [InlineData("Practitioner", 403)]
    [InlineData("Physician", 403)]
    [InlineData("Administrator", 403)]
    [InlineData("DirectoryAdministrator", 403)]
    [InlineData("IntegrationAdministrator", 403)]
    public async Task AuditPolicyUsesOnlyExplicitAuditRoles(string role, int expected)
    {
        using var client = await Reader(role);
        using var response = await client.GetAsync("/api/v1/admin/audit");
        ((int)response.StatusCode).Should().Be(expected);
        response.Headers.Contains("X-Correlation-ID").Should().BeTrue();
    }

    [Fact]
    public async Task AnonymousAuditRequestRequiresAuthentication()
    {
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/admin/audit");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuditPagesUseStablePostgresOrderWithoutDuplicatesAndNeverIncludeForeignEvents()
    {
        var correlation = Guid.NewGuid().ToString("N");
        var time = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        var rows = Enumerable.Range(0, 5).Select(_ => Event(correlation, time)).ToArray();
        await using (var db = fixture.CreateContext())
        {
            var foreign = OrganizationId.New();
            db.Organizations.Add(Organization.CreateSimulation(foreign, "Fictional Phase 10 Scope", time));
            db.AuditEvents.Add(Event(correlation, time.AddDays(1), organization: foreign));
            db.AuditEvents.AddRange(rows);
            await db.SaveChangesAsync();
        }
        using var client = await Reader("Auditor");
        var collected = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var path = "/api/v1/admin/audit?correlationId=" + correlation + "&pageSize=2"
                + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var page = await client.GetFromJsonAsync<AuditPage>(path);
            page!.Events.Count.Should().BeInRange(1, 2);
            collected.AddRange(page.Events.Select(e => e.Id));
            cursor = page.NextCursor;
            pages++;
            pages.Should().BeLessThan(5);
        } while (cursor is not null);
        collected.Should().Equal(rows.OrderByDescending(e => e.Id.Value).Select(e => e.Id.Value));
        pages.Should().Be(3);
    }

    [Fact]
    public async Task AuditFiltersApplyTogetherWithInclusiveStartAndExclusiveEnd()
    {
        var correlation = Guid.NewGuid().ToString("N");
        var from = DateTimeOffset.Parse("2026-09-01T00:00:00Z");
        var expected = Event(correlation, from);
        await using (var db = fixture.CreateContext())
        {
            db.AuditEvents.AddRange(expected, Event(correlation, from.AddTicks(-10)), Event(correlation, from.AddDays(1)),
                Event(correlation, from, action: "alert.draft.created"), Event(correlation, from, outcome: "failed"),
                Event(correlation, from, resourceType: "delivery-attempt"), Event(Guid.NewGuid().ToString("N"), from));
            await db.SaveChangesAsync();
        }
        using var client = await Reader("Auditor");
        var page = await client.GetFromJsonAsync<AuditPage>("/api/v1/admin/audit?correlationId=" + correlation
            + "&occurredFromUtc=2026-09-01T00%3A00%3A00Z&occurredToUtc=2026-09-02T00%3A00%3A00Z"
            + "&action=alert.confirmed&outcome=succeeded&resourceType=alert");
        page!.Events.Select(e => e.Id).Should().Equal(expected.Id.Value);
    }

    [Theory]
    [InlineData("cursor=!")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=invalid")]
    [InlineData("occurredFromUtc=invalid")]
    [InlineData("occurredFromUtc=2026-09-02T00:00:00Z&occurredToUtc=2026-09-01T00:00:00Z")]
    [InlineData("organizationId=11111111-1111-4111-8111-111111111111")]
    [InlineData("sort=action")]
    public async Task InvalidQueriesReturnSafeProblems(string query)
    {
        using var client = await Reader("Auditor");
        using var response = await client.GetAsync("/api/v1/admin/audit?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Be("Audit query is invalid. Review the filters and pagination.");
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task ReadIsAuditedAfterProjectionWithOnlyFilterNamesAndCounts()
    {
        var effective = Guid.NewGuid().ToString("N");
        var filter = Guid.NewGuid().ToString("N");
        using var client = await Reader("Auditor");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", effective);
        var page = await client.GetFromJsonAsync<AuditPage>("/api/v1/admin/audit?correlationId=" + filter + "&pageSize=3");
        page!.Events.Should().BeEmpty();
        await using var db = fixture.CreateContext();
        var recorded = await db.AuditEvents.SingleAsync(e => e.Action == "audit.read" && e.CorrelationId == effective);
        recorded.OrganizationId.Should().Be(DemoDataSeeder.OrganizationId);
        recorded.ActorUserId.Should().NotBeNull();
        var metadata = JsonDocument.Parse(recorded.SanitizedMetadata).RootElement;
        metadata.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("pageSize", "filtersUsed", "resultCount");
        metadata.GetProperty("pageSize").GetInt32().Should().Be(3);
        metadata.GetProperty("resultCount").GetInt32().Should().Be(0);
        metadata.GetProperty("filtersUsed")[0].GetString().Should().Be("correlationId");
        recorded.SanitizedMetadata.Contains(filter, StringComparison.Ordinal).Should().BeFalse();
    }

    [Fact]
    public async Task ProtectedMetadataAndUntrustedTopLevelValuesNeverReachAuditOutput()
    {
        var correlation = Guid.NewGuid().ToString("N");
        var sentinel = "SIM-PATIENT-PHASE10-SENTINEL";
        await using (var db = fixture.CreateContext())
        {
            db.AuditEvents.Add(Event(correlation, DateTimeOffset.UtcNow, metadata:
                JsonSerializer.Serialize(new
                {
                    version = 2,
                    channel = sentinel,
                    patientReference = sentinel,
                    message = sentinel,
                    nested = new { value = sentinel }
                })));
            await db.SaveChangesAsync();
        }
        using var client = await Reader("Auditor");
        using var response = await client.GetAsync("/api/v1/admin/audit?correlationId=" + correlation);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        body.Contains(sentinel, StringComparison.Ordinal).Should().BeFalse("protected payload cannot appear in audit output");
        var page = JsonSerializer.Deserialize<AuditPage>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        page!.Events.Single().Metadata.Keys.Should().Equal("version");
        body.Contains("sanitizedMetadata", StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    private async Task<HttpClient> Reader(string role)
    {
        await using var db = fixture.CreateContext();
        var user = UserId.New();
        var handle = "sim-audit-" + Guid.NewGuid().ToString("N");
        db.Users.Add(UserAccount.CreateSimulation(user, DemoDataSeeder.OrganizationId, "Fictional Audit Test", handle, DateTimeOffset.UtcNow));
        var assignedRole = await db.Roles.SingleAsync(r => r.OrganizationId == DemoDataSeeder.OrganizationId && r.Name == role);
        db.UserRoles.Add(UserRole.Create(DemoDataSeeder.OrganizationId, user, assignedRole.Id));
        await db.SaveChangesAsync();
        return await fixture.CreateSignedInClientAsync(handle);
    }

    private static AuditEvent Event(string correlation, DateTimeOffset time, string action = "alert.confirmed",
        string outcome = "succeeded", string resourceType = "alert", string metadata = "{}", OrganizationId? organization = null)
        => AuditEvent.Record(AuditEventId.New(), organization ?? DemoDataSeeder.OrganizationId, "user",
            DemoDataSeeder.JordanUserId, action, resourceType, Guid.NewGuid(), outcome, correlation, metadata, time);
}
