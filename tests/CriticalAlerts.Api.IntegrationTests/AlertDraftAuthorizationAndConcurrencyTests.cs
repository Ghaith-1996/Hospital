using System.Net;
using System.Net.Http.Json;
using CriticalAlerts.Application.Alerts;
using CriticalAlerts.Application.Directory;
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

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle)]
    [InlineData(DemoDataSeeder.MorganHandle)]
    public async Task AuthorizedDraftEditorCanCreateAndReadAProtectedTypedDraft(string simulationHandle)
    {
        using var client = await fixture.CreateSignedInClientAsync(simulationHandle);

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
        submittedBody.State.Should().NotBe("DispatchQueued");
    }

    [Fact]
    public async Task CriticalFieldConfirmationIsBoundToExactValueUnitAndDraftVersion()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var request = ValidCreateRequest() with
        {
            SourceText = "SIMULATION: original operator source records BP 88/54 mmHg",
            Sbar = ValidCreateRequest().Sbar! with
            {
                Situation = "SIMULATION: structured SBAR records fictional BP for review",
            },
            CriticalFields = [new AlertCriticalFieldInput("bloodPressure", "88/54", "mmHg")],
        };
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", request);
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var changedValue = await client.PostAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "bloodPressure", "86/52", "86/52", "mmHg"));
        using var changedUnit = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "bloodPressure", "88/54", "88/54", "kPa"));

        changedValue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        changedUnit.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var exact = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "bloodPressure", "88/54", "88/54", "mmHg"));
        var confirmed = await exact.Content.ReadFromJsonAsync<AlertDraftView>();

        exact.StatusCode.Should().Be(HttpStatusCode.OK);
        confirmed!.CriticalFields.Should().ContainSingle(field =>
            field.AlertVersion == draft.DraftVersion
            && field.OriginalValue == "88/54"
            && field.NormalizedValue == "88/54"
            && field.Unit == "mmHg"
            && field.Status == "Confirmed");

        using var read = await client.GetAsync($"/api/alerts/{draft.AlertId:D}");
        var reloaded = await read.Content.ReadFromJsonAsync<AlertDraftView>();
        reloaded!.SourceText.Should().Be(request.SourceText);
        reloaded.Sbar!.Situation.Should().Be(request.Sbar!.Situation);
    }

    [Fact]
    public async Task EditingConfirmedCriticalContentInvalidatesConfirmationAndRejectsOldVersionCommands()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var request = ValidCreateRequest() with
        {
            CriticalFields = [new AlertCriticalFieldInput("bloodPressure", "88/54", "mmHg")],
        };
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", request);
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        using var confirm = await client.PostAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "bloodPressure", "88/54", "88/54", "mmHg"));
        confirm.StatusCode.Should().Be(HttpStatusCode.OK);

        var updateRequest = ValidUpdateRequest(draft.DraftVersion) with
        {
            SourceText = "SIMULATION: revised typed source with fictional BP 86/52",
            Sbar = ValidUpdateRequest(draft.DraftVersion).Sbar! with
            {
                Situation = "SIMULATION: revised fictional situation with BP 86/52",
            },
            CriticalFields = [new AlertCriticalFieldInput("bloodPressure", "86/52", "mmHg")],
        };
        using var update = await client.PatchAsJsonAsync($"/api/alerts/{draft.AlertId:D}", updateRequest);
        var edited = await update.Content.ReadFromJsonAsync<AlertDraftView>();

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        edited!.DraftVersion.Should().Be(draft.DraftVersion + 1);
        edited.State.Should().Be("Draft");
        edited.CriticalFields.Should().ContainSingle(field =>
            field.AlertVersion == edited.DraftVersion
            && field.OriginalValue == "86/52"
            && field.Unit == "mmHg"
            && field.Status == "Unresolved");

        using var staleConfirmation = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "bloodPressure", "88/54", "88/54", "mmHg"));
        using var staleSubmission = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(draft.DraftVersion));
        using var unresolvedSubmission = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(edited.DraftVersion));

        staleConfirmation.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleSubmission.StatusCode.Should().Be(HttpStatusCode.Conflict);
        unresolvedSubmission.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var confirmEditedValue = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(edited.DraftVersion, "bloodPressure", "86/52", "86/52", "mmHg"));
        confirmEditedValue.StatusCode.Should().Be(HttpStatusCode.OK);

        using var unitUpdate = await client.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            updateRequest with
            {
                ExpectedVersion = edited.DraftVersion,
                CriticalFields = [new AlertCriticalFieldInput("bloodPressure", "86/52", "kPa")],
            });
        var unitEdited = await unitUpdate.Content.ReadFromJsonAsync<AlertDraftView>();

        unitUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        unitEdited!.DraftVersion.Should().Be(edited.DraftVersion + 1);
        unitEdited.CriticalFields.Should().ContainSingle(field =>
            field.AlertVersion == unitEdited.DraftVersion
            && field.OriginalValue == "86/52"
            && field.Unit == "kPa"
            && field.Status == "Unresolved");
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

        var foreignAlertId = await fixture.CreateForeignDraftAsync();
        using var foreign = await client.GetAsync($"/api/alerts/{foreignAlertId:D}");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().NotContain("organization_id");
    }

    [Fact]
    public async Task DraftAuthorizationRejectsAnonymousPractitionerAndForeignOrganizationAccess()
    {
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await operatorClient.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        using var anonymous = fixture.CreateClient();
        using var anonymousRead = await anonymous.GetAsync($"/api/alerts/{draft!.AlertId:D}");
        using var anonymousUpdate = await anonymous.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            ValidUpdateRequest(draft.DraftVersion));
        using var anonymousConfirm = await anonymous.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(draft.DraftVersion, "heartRate", "118", "118", "beats/min"));
        using var anonymousSubmit = await anonymous.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(draft.DraftVersion));

        anonymousRead.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousUpdate.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousConfirm.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousSubmit.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var practitionerUpdate = await practitioner.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            new
            {
                organizationId = DemoDataSeeder.OrganizationId.Value,
                roles = new[] { "Operator" },
                expectedVersion = draft.DraftVersion,
                location = "North Wing / Simulation Room 205",
                urgencyLabel = "Urgent",
                sourceText = "SIMULATION: practitioner impersonation attempt",
                sbar = ValidUpdateRequest(draft.DraftVersion).Sbar,
                criticalFields = ValidUpdateRequest(draft.DraftVersion).CriticalFields,
            });
        practitionerUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignFixture = await fixture.CreateForeignOperatorDraftAsync();
        using var foreignClient = await fixture.CreateSignedInClientAsync(foreignFixture.SimulationHandle);
        using var foreignRead = await foreignClient.GetAsync($"/api/alerts/{draft.AlertId:D}");
        using var foreignUpdate = await foreignClient.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            new
            {
                organizationId = DemoDataSeeder.OrganizationId.Value,
                expectedVersion = draft.DraftVersion,
                location = "North Wing / Simulation Room 205",
                urgencyLabel = "Urgent",
                sourceText = "SIMULATION: foreign organization override attempt",
                sbar = ValidUpdateRequest(draft.DraftVersion).Sbar,
                criticalFields = ValidUpdateRequest(draft.DraftVersion).CriticalFields,
            });

        foreignRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreignUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ClinicalDraftPayloadIsAbsentFromGeneralApiLogsAndSafeErrors()
    {
        const string patientSentinel = "SIM-PAT-LOG-NONDISCLOSURE";
        const string sourceSentinel = "SIMULATION: LOG-SOURCE-NONDISCLOSURE";
        const string situationSentinel = "SIMULATION: LOG-SITUATION-NONDISCLOSURE";
        const string backgroundSentinel = "SIMULATION: LOG-BACKGROUND-NONDISCLOSURE";
        const string assessmentSentinel = "SIMULATION: LOG-ASSESSMENT-NONDISCLOSURE";
        const string recommendationSentinel = "SIMULATION: LOG-RECOMMENDATION-NONDISCLOSURE";
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        fixture.ClearLogs();

        var request = ValidCreateRequest() with
        {
            SiteId = Guid.NewGuid(),
            SimulationPatientReference = patientSentinel,
            SourceText = sourceSentinel,
            Sbar = new AlertSbarDraft(
                situationSentinel,
                backgroundSentinel,
                assessmentSentinel,
                recommendationSentinel),
        };
        using var response = await client.PostAsJsonAsync("/api/alerts/drafts", request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var logs = fixture.LogEntries;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        logs.Should().NotBeEmpty();
        foreach (var sentinel in new[]
                 {
                     patientSentinel,
                     sourceSentinel,
                     situationSentinel,
                     backgroundSentinel,
                     assessmentSentinel,
                     recommendationSentinel,
                 })
        {
            responseBody.Should().NotContain(sentinel);
            logs.Should().NotContain(entry => entry.Contains(sentinel, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PhaseFiveEndpointsCannotReachDispatchQueued()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await client.PostAsJsonAsync("/api/alerts/drafts", ValidCreateRequest());
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        draft!.State.Should().Be("Draft");

        using var update = await client.PatchAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}",
            ValidUpdateRequest(draft.DraftVersion));
        var edited = await update.Content.ReadFromJsonAsync<AlertDraftView>();
        edited!.State.Should().Be("Draft");

        using var confirm = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/field-confirmations",
            new ConfirmAlertCriticalFieldRequest(edited.DraftVersion, "heartRate", "118", "118", "beats/min"));
        var confirmed = await confirm.Content.ReadFromJsonAsync<AlertDraftView>();
        confirmed!.State.Should().Be("Draft");

        using var submit = await client.PostAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/submit-for-confirmation",
            new SubmitAlertDraftRequest(confirmed.DraftVersion));
        var submitted = await submit.Content.ReadFromJsonAsync<AlertDraftView>();

        submitted!.State.Should().Be("PendingConfirmation");
        submitted.State.Should().NotBe("DispatchQueued");
        (await fixture.GetAlertStateAsync(draft.AlertId)).Should().Be("PendingConfirmation");
    }

    [Fact]
    public async Task DraftRejectsMissingRequiredSbarContent()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        var incomplete = ValidCreateRequest() with
        {
            Sbar = ValidCreateRequest().Sbar! with { Situation = "" },
        };

        using var response = await client.PostAsJsonAsync("/api/alerts/drafts", incomplete);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("required-field");
        body.Should().NotContain("Ciphertext");
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

    [Theory]
    [InlineData(DemoDataSeeder.JordanHandle)]
    [InlineData(DemoDataSeeder.MorganHandle)]
    public async Task AuthorizedEditorCanSetApprovedMessageReplaceAndClearRecipientSet(string simulationHandle)
    {
        using var client = await fixture.CreateSignedInClientAsync(simulationHandle);
        using var create = await client.PostAsJsonAsync(
            "/api/alerts/drafts",
            ValidCreateRequest() with { CriticalFields = [] });
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();

        const string approvedMessage = "SIMULATION: operator-approved fictional message";
        using var approved = await client.PutAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, approvedMessage));
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        approvedDraft!.DraftVersion.Should().Be(draft.DraftVersion + 1);
        approvedDraft.ApprovedMessage.Should().Be(approvedMessage);
        approvedDraft.Recipients.Should().BeEmpty();
        var approvedBody = await approved.Content.ReadAsStringAsync();
        approvedBody.Should().NotContain("Ciphertext");
        approvedBody.Should().NotContain("protectedValue");

        var maya = await SearchMayaAsync(client);
        using var replace = await client.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(
                approvedDraft.DraftVersion,
                [new AlertRecipientInput(
                    maya.PractitionerId,
                    maya.PractitionerRoleId,
                    "SecureMessage",
                    maya.SelectionRevision)]));
        var replaced = await replace.Content.ReadFromJsonAsync<AlertDraftView>();

        replace.StatusCode.Should().Be(HttpStatusCode.OK);
        replaced!.DraftVersion.Should().Be(approvedDraft.DraftVersion + 1);
        replaced.Recipients.Should().ContainSingle(recipient =>
            recipient.PractitionerId == maya.PractitionerId
            && recipient.Channel == "SecureMessage"
            && recipient.DirectoryRevision == maya.SelectionRevision);

        using var clear = await client.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/recipients",
            new ReplaceAlertRecipientsRequest(replaced.DraftVersion, []));
        var cleared = await clear.Content.ReadFromJsonAsync<AlertDraftView>();

        clear.StatusCode.Should().Be(HttpStatusCode.OK);
        cleared!.DraftVersion.Should().Be(replaced.DraftVersion + 1);
        cleared.Recipients.Should().BeEmpty();
    }

    [Fact]
    public async Task RecipientCommandsRejectUnauthorizedForeignAndStaleRequests()
    {
        using var operatorClient = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await operatorClient.PostAsJsonAsync(
            "/api/alerts/drafts",
            ValidCreateRequest() with { CriticalFields = [] });
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        var message = new SetApprovedMessageRequest(draft!.DraftVersion, "SIMULATION: authorization boundary message");

        using var anonymous = fixture.CreateClient();
        using var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            message);
        using var practitioner = await fixture.CreateSignedInClientAsync(DemoDataSeeder.RileyHandle);
        using var practitionerResponse = await practitioner.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            message);

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        practitionerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var foreignFixture = await fixture.CreateForeignOperatorDraftAsync();
        using var foreign = await fixture.CreateSignedInClientAsync(foreignFixture.SimulationHandle);
        using var foreignResponse = await foreign.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            new { expectedVersion = draft.DraftVersion, approvedMessage = "SIMULATION: foreign access" });
        foreignResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var ignoredOrganization = await operatorClient.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            new
            {
                organizationId = Guid.NewGuid(),
                expectedVersion = draft.DraftVersion,
                approvedMessage = "SIMULATION: client organization must be ignored",
            });
        ignoredOrganization.StatusCode.Should().Be(HttpStatusCode.OK);

        using var stale = await operatorClient.PutAsJsonAsync(
            $"/api/alerts/{draft.AlertId:D}/approved-message",
            message);
        var staleBody = await stale.Content.ReadAsStringAsync();

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        staleBody.Should().Contain("draft-version-stale");
        staleBody.Should().NotContain("authorization boundary message");
    }

    [Fact]
    public async Task RecipientReplacementRejectsUnsafeSelectionsWithoutEchoingInput()
    {
        using var client = await fixture.CreateSignedInClientAsync(DemoDataSeeder.JordanHandle);
        using var create = await client.PostAsJsonAsync(
            "/api/alerts/drafts",
            ValidCreateRequest() with { CriticalFields = [] });
        var draft = await create.Content.ReadFromJsonAsync<AlertDraftView>();
        var maya = await SearchMayaAsync(client);
        var taylor = await SearchTaylorAsync(client);

        using var approved = await client.PutAsJsonAsync(
            $"/api/alerts/{draft!.AlertId:D}/approved-message",
            new SetApprovedMessageRequest(draft.DraftVersion, "SIMULATION: recipient validation message"));
        var approvedDraft = await approved.Content.ReadFromJsonAsync<AlertDraftView>();

        var duplicate = new ReplaceAlertRecipientsRequest(
            approvedDraft!.DraftVersion,
            [
                new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "SecureMessage", maya.SelectionRevision),
                new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "SecureMessage", maya.SelectionRevision),
            ]);
        using var duplicateResponse = await client.PutAsJsonAsync($"/api/alerts/{draft.AlertId:D}/recipients", duplicate);
        var duplicateBody = await duplicateResponse.Content.ReadAsStringAsync();

        var inactive = new ReplaceAlertRecipientsRequest(
            approvedDraft.DraftVersion,
            [new AlertRecipientInput(taylor.PractitionerId, taylor.PractitionerRoleId, "SecureMessage", taylor.SelectionRevision)]);
        using var inactiveResponse = await client.PutAsJsonAsync($"/api/alerts/{draft.AlertId:D}/recipients", inactive);

        var unavailable = new ReplaceAlertRecipientsRequest(
            approvedDraft.DraftVersion,
            [new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "Voice", maya.SelectionRevision)]);
        using var unavailableResponse = await client.PutAsJsonAsync($"/api/alerts/{draft.AlertId:D}/recipients", unavailable);

        const string unsafeRevision = "revision-with-sensitive-sentinel-SIM-PAT-RECIPIENT!";
        var unsafeRequest = new ReplaceAlertRecipientsRequest(
            approvedDraft.DraftVersion,
            [new AlertRecipientInput(maya.PractitionerId, maya.PractitionerRoleId, "SecureMessage", unsafeRevision)]);
        using var unsafeResponse = await client.PutAsJsonAsync($"/api/alerts/{draft.AlertId:D}/recipients", unsafeRequest);
        var unsafeBody = await unsafeResponse.Content.ReadAsStringAsync();

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        duplicateBody.Should().Contain("duplicate-recipient");
        inactiveResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unavailableResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unsafeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unsafeBody.Should().NotContain(unsafeRevision);
    }

    private static async Task<DirectoryPractitionerListItem> SearchMayaAsync(HttpClient client)
    {
        var results = await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/directory/practitioners?q=Maya&includeInactive=false");
        return results!.Single();
    }

    private static async Task<DirectoryPractitionerListItem> SearchTaylorAsync(HttpClient client)
    {
        var results = await client.GetFromJsonAsync<DirectoryPractitionerListItem[]>(
            "/api/directory/practitioners?q=Taylor&includeInactive=true");
        return results!.Single();
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
                "SIMULATION: revised recommendation"),
            [new AlertCriticalFieldInput("heartRate", "118", "beats/min")]);
}
