using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertDraftAuthorizationAndConcurrencyTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task UnauthenticatedDraftCreationReturnsUnauthorized()
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        body.Should().Contain("authentication-required");
        body.Should().NotContain("Administrator");
    }

    [Fact]
    public async Task PractitionerCannotCreateAnAlertDraft()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);

        using var response = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OperatorCanCreateAndReadAProtectedTypedDraft()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        using var response = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await response.Content.ReadFromJsonAsync<AlertDraftView>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        draft.Should().NotBeNull();
        draft!.State.Should().Be("Draft");
        draft.DraftVersion.Should().Be(1);
        draft.SourceText.Should().Be("SIMULATION: fictional typed source");
        draft.Sbar!.Situation.Should().Be("SIMULATION: fictional situation");
        draft.CriticalFields.Should().ContainSingle(field => field.Status == "Unresolved");
        var responseText = await response.Content.ReadAsStringAsync();
        responseText.Should().NotContain("Ciphertext");
        responseText.Should().NotContain("protectedValue");

        using var read = await client.GetAsync($"/api/alerts/{draft.AlertId:D}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DraftRequiresCriticalFieldConfirmationBeforeSubmission()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var submit = await client.PostAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(draft.DraftVersion));
        var submitBody = await submit.Content.ReadAsStringAsync();

        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        submitBody.Should().Contain("alert-draft-rejected");
        submitBody.Should().NotContain("Ciphertext");

        using var confirm = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "heartRate", "118", "118", "beats/min"));
        var confirmed = await confirm.Content.ReadFromJsonAsync<AlertDraftView>();

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        confirmed!.CriticalFields.Should().ContainSingle(field => field.Status == "Confirmed");

        using var submitted = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmed.DraftVersion));
        var submittedBody = await submitted.Content.ReadFromJsonAsync<AlertDraftView>();

        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        submittedBody!.State.Should().Be("PendingConfirmation");
    }

    [Fact]
    public async Task DraftEditsRequireTheCurrentVersionAndRemainOrganizationScoped()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var stale = await client.PatchAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}",
            ValidUpdateRequest(expectedVersion: draft.DraftVersion - 1));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("draft-version-stale");

        using var update = await client.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            ValidUpdateRequest(expectedVersion: draft.DraftVersion));
        var updated = await update.Content.ReadFromJsonAsync<AlertDraftView>();

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        updated!.DraftVersion.Should().Be(draft.DraftVersion + 1);
        updated.Location.Should().Be("North Wing / Simulation Room 205");

        using var foreign = await client.GetAsync($"/api/alerts/{Guid.NewGuid():D}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain("organization_id");
    }

    [Fact]
    public async Task DraftRejectsNonSimulationContentAndInvalidOrganizationLocation()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);

        var nonSimulation = ValidCreateRequest() with { SourceText = "a real note" };
        using var sourceResponse = await client.PostAsJsonAsync("/api/alerts/drafts", nonSimulation);
        var sourceBody = await sourceResponse.Content.ReadAsStringAsync();

        sourceResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sourceBody.Should().Contain("non-simulation-content");
        sourceBody.Should().NotContain("a real note");

        var invalidLocation = ValidCreateRequest() with { SiteId = Guid.NewGuid() };
        using var locationResponse = await client.PostAsJsonAsync("/api/alerts/drafts", invalidLocation);

        locationResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await locationResponse.Content.ReadAsStringAsync()).Should().Contain("invalid-location");
    }

    private static CreateAlertDraftRequest ValidCreateRequest()
        => new(
            DemoDataSeeder.NorthSiteId.Value,
            DemoDataSeeder.EmergencyDepartmentId.Value,
            "SIM-PAT-0001",
            "North Wing / Simulation Room 204",
            "Urgent",
            "SIMULATION: fictional typed source",
            new AlertSbarDraft(
                "SIMULATION: fictional situation",
                "SIMULATION: fictional background",
                "SIMULATION: fictional assessment",
                "SIMULATION: fictional recommendation"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]);

    private static UpdateAlertDraftRequest ValidUpdateRequest(int expectedVersion)
        => new(
            expectedVersion,
            "North Wing / Simulation Room 205",
            "Emergent",
            "SIMULATION: revised typed source",
            new AlertSbarDraft(
                "SIMULATION: revised situation",
                "SIMULATION: revised background",
                "SIMULATION: revised assessment",
                "SIMULATION: revised recommendation"));
}
