using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace CriticalAlerts.Api.IntegrationTests;

[Collection(SeededPostgresApiCollection.Name)]
public sealed class AlertReviewTests(SeededPostgresApiFixture fixture)
{
    [Fact]
    public async Task ExactReviewContainsOnlyTheCurrentCompleteVersion()
    {
        const string patientReference = "SIM-PAT-REVIEW-0001";
        const string sourceText = "SIMULATION: review source sentinel";
        const string approvedMessage = "SIMULATION: exact approved message sentinel";
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var request = ValidCreateRequest() with
        {
            SimulationPatientReference = patientReference,
            SourceText = sourceText,
            Sbar = new AlertSbarDraft(
                "SIMULATION: review situation sentinel",
                "SIMULATION: review background sentinel",
                "SIMULATION: review assessment sentinel",
                "SIMULATION: review recommendation sentinel"),
            CriticalFields = [new AlertCriticalFieldInput("bloodPressure", "88/54", "mmHg")],
        };

        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", request);
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        using var approved = await client.PutAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, approvedMessage));
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();
        var maya = await SearchMayaAsync(client);
        using var recipients = await client.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(
                approvedDraft!.DraftVersion,
                [new AlertRecipientInput(
                    maya.PractitionerId,
                    maya.PractitionerRoleId,
                    "SecureMessage",
                    maya.SelectionRevision)]));
        var recipientDraft = await recipients.Content.ReadFromJsonAsync<AlertDraftView>();
        using var fieldConfirmation = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(
                recipientDraft!.DraftVersion,
                "bloodPressure",
                "88/54",
                "88/54",
                "mmHg"));
        var confirmedDraft = await fieldConfirmation.Content.ReadFromJsonAsync<AlertDraftView>();
        using var submit = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmedDraft!.DraftVersion));

        using var reviewResponse = await client.GetAsync($"/api/alerts/{draft.AlertId:D}/review");
        var reviewBody = await reviewResponse.Content.ReadAsStringAsync();
        var review = await reviewResponse.Content.ReadFromJsonAsync<AlertReviewView>();

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        recipients.StatusCode.Should().Be(HttpStatusCode.OK);
        fieldConfirmation.StatusCode.Should().Be(HttpStatusCode.OK);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        review.Should().NotBeNull();
        review!.AlertId.Should().Be(draft.AlertId);
        review.DraftVersion.Should().Be(recipientDraft.DraftVersion);
        review.State.Should().Be("PendingConfirmation");
        review.SimulationPatientReference.Should().Be(patientReference);
        review.Location.Should().Be(request.Location);
        review.UrgencyLabel.Should().Be(request.UrgencyLabel);
        review.ApprovedMessage.Should().Be(approvedMessage);
        review.CriticalFields.Should().ContainSingle(field =>
            field.AlertVersion == review.DraftVersion
            && field.FieldId == "bloodPressure"
            && field.OriginalValue == "88/54"
            && field.NormalizedValue == "88/54"
            && field.Unit == "mmHg"
            && field.Status == "Confirmed"
            && field.ConfirmedByUserId == fixture.JordanUserId);
        review.Recipients.Should().ContainSingle();
        var reviewRecipient = review.Recipients.Single();
        reviewRecipient.PractitionerId.Should().Be(maya.PractitionerId);
        reviewRecipient.DisplayName.Should().Be("Maya Chen");
        reviewRecipient.Specialty.Should().Be("Emergency");
        reviewRecipient.Department.Should().Be("Fictional Emergency Care");
        reviewRecipient.Site.Should().Be("North Wing Simulation Site");
        reviewRecipient.RoleTitle.Should().Be("Emergency physician");
        reviewRecipient.Channel.Should().Be("SecureMessage");
        reviewRecipient.DirectoryRevision.Should().Be(maya.SelectionRevision);
        reviewRecipient.DirectorySourceUpdatedAtUtc.Should().Be(DateTimeOffset.Parse("2026-08-01T12:00:00Z"));
        reviewRecipient.IsStale.Should().BeFalse();
        review.DemoEscalationPolicyVersion.Should().Be("DEMO");
        review.DemoNotificationPolicyVersion.Should().Be("DEMO");
        reviewBody.Should().NotContain("sim-secure://");
        reviewBody.Should().NotContain("+1 555");
    }

    [Fact]
    public async Task ReviewRequiresPendingCompleteCurrentVersionAndReturnsSafeConflict()
    {
        const string approvedSentinel = "SIMULATION: review-not-ready approved sentinel";
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var draftReview = await client.GetAsync($"/api/alerts/{draft!.AlertId:D}/review");
        var draftReviewBody = await draftReview.Content.ReadAsStringAsync();

        using var approved = await client.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, approvedSentinel));
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();
        using var incompleteReview = await client.GetAsync($"/api/alerts/{draft.AlertId:D}/review");
        var incompleteBody = await incompleteReview.Content.ReadAsStringAsync();

        draftReview.StatusCode.Should().Be(HttpStatusCode.Conflict);
        draftReviewBody.Should().Contain("review-not-ready");
        draftReviewBody.Should().NotContain(approvedSentinel);
        incompleteReview.StatusCode.Should().Be(HttpStatusCode.Conflict);
        incompleteBody.Should().Contain("review-not-ready");
        incompleteBody.Should().NotContain(approvedSentinel);

        using var edited = await client.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            ValidUpdateRequest(approvedDraft!.DraftVersion) with
            {
                CriticalFields = [new AlertCriticalFieldInput("heartRate", "117", "beats/min")],
            });
        using var changedVersionReview = await client.GetAsync($"/api/alerts/{draft.AlertId:D}/review");

        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        changedVersionReview.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await changedVersionReview.Content.ReadAsStringAsync()).Should().Contain("review-not-ready");
    }

    [Fact]
    public async Task ReviewIsOrganizationScopedAndAuthorizationProtected()
    {
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await operatorClient.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync($"/api/alerts/{draft!.AlertId:D}/review");
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var practitionerResponse = await practitioner.GetAsync($"/api/alerts/{draft.AlertId:D}/review");

        var foreignFixture = await fixture.CreateForeignOperatorDraftAsync();
        using var foreign = await fixture.CreateSignedInClientAsync(foreignFixture.SimulationHandle);
        using var foreignResponse = await foreign.GetAsync($"/api/alerts/{draft.AlertId:D}/review");

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        practitionerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreignResponse.Content.ReadAsStringAsync()).Should().NotContain("organization_id");
    }

    private static async Task<DirectoryPractitionerListItem> SearchMayaAsync(HttpClient client)
    {
        var results = await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/directory/practitioners?q=Maya&includeInactive=false");
        return results!.Single();
    }

    private static CreateAlertDraftRequest ValidCreateRequest()
        => new(
            DemoDataSeeder.NorthSiteId.Value,
            DemoDataSeeder.EmergencyDepartmentId.Value,
            "SIM-PAT-REVIEW-BASE",
            "North Wing / Simulation Room 204",
            "Urgent",
            "SIMULATION: review source",
            new AlertSbarDraft(
                "SIMULATION: review situation",
                "SIMULATION: review background",
                "SIMULATION: review assessment",
                "SIMULATION: review recommendation"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]);

    private static UpdateAlertDraftRequest ValidUpdateRequest(int expectedVersion)
        => new(
            expectedVersion,
            "North Wing / Simulation Room 205",
            "Emergent",
            "SIMULATION: revised review source",
            new AlertSbarDraft(
                "SIMULATION: revised review situation",
                "SIMULATION: revised review background",
                "SIMULATION: revised review assessment",
                "SIMULATION: revised review recommendation"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]);
}
