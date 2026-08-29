# Phase 6 Recipient Selection and Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add simulation-only manual recipient selection, separately protected approved-message content, exact review, and safe idempotent human confirmation without adding dispatch processing.

**Architecture:** Extend the existing modular monolith and fictional directory. Every editable choice creates one exact `DraftVersion`; recipient selection is a full-set replacement and stores a safe directory snapshot. Confirmation is one PostgreSQL transaction containing the state transition, sanitized audit, completed idempotency record, and identifier-only outbox item. Phase 7 alone will lease/process outbox items and call providers.

**Tech Stack:** C# 14, ASP.NET Core/.NET 10, EF Core 10, Npgsql 10, PostgreSQL 18 Testcontainers, TypeScript, React 19, Next.js 16 App Router, Vitest, Playwright.

**Spec:** `docs/architecture/recipient-selection-and-review.md`

**Phase boundary:** This plan creates an `AlertDispatchRequested` outbox row because it is part of the atomic confirmation invariant. It does not alter `CriticalAlerts.Worker`, create a delivery attempt, lease/process an outbox row, call a provider, retry, escalate, expose callbacks, or add `/alerts/{id}/live`.

## Execution status

The plan was approved for inline execution. Its Phase 6 implementation is complete locally and is awaiting project-owner review. The backend slices are implemented and the web/docs changes are in this worktree; the final handoff records the pinned-container formatting and linked-worktree architecture-test limitations. No Phase 7 behavior has been started.

---

## Task 1: Make alert content and recipients exact-version snapshots

**Files:**

- Modify: `src/backend/CriticalAlerts.Domain/Alerts/Alert.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Alerts/AlertRecipientSelection.cs`
- Modify: `tests/CriticalAlerts.Domain.Tests/AlertStateMachineTests.cs`

- [ ] **Step 1: Write failing domain tests**

Add tests proving that replacing two recipients increments the version once, both rows use the new version, duplicate practitioner/channel pairs fail before mutation, an empty replacement clears current recipients, content and approved-message edits carry the current recipients to the new version, and every version-changing edit makes critical fields unresolved.

```csharp
[Fact]
public void ReplacingRecipientSetCreatesOneExactVersion()
{
    var alert = CreateConfirmableDraft();
    var before = alert.DraftVersion;

    alert.ReplaceRecipients(
        [Recipient(Maya, NotificationChannel.SecureMessage), Recipient(Noah, NotificationChannel.Voice)],
        OperatorId,
        before,
        Now);

    alert.DraftVersion.Value.Should().Be(before.Value + 1);
    alert.CurrentRecipients.Should().HaveCount(2)
        .And.OnlyContain(item => item.AlertVersion == alert.DraftVersion);
    alert.FieldConfirmations
        .Where(item => item.AlertVersion == alert.DraftVersion)
        .Should().OnlyContain(item => item.Status == FieldConfirmationStatus.Unresolved);
}
```

- [ ] **Step 2: Run the focused domain tests and confirm the new tests fail**

Run: `dotnet test tests/CriticalAlerts.Domain.Tests/CriticalAlerts.Domain.Tests.csproj -c Release --filter AlertStateMachineTests`

Expected: FAIL because `ReplaceRecipients` and the version snapshot behavior do not exist.

- [ ] **Step 3: Replace the single-recipient mutation with one full-set domain command**

Add a domain input that has already passed directory validation, expand the persisted selection snapshot, and expose one mutation:

```csharp
public sealed record ValidatedRecipientSelection(
    PractitionerId PractitionerId,
    PractitionerRoleId? PractitionerRoleId,
    NotificationChannel Channel,
    string DirectoryRevision,
    DateTimeOffset? DirectorySourceUpdatedAtUtc,
    string? OnCallSnapshot);

public void ReplaceRecipients(
    IReadOnlyCollection<ValidatedRecipientSelection> recipients,
    UserId selectedByUserId,
    AlertDraftVersion expectedVersion,
    DateTimeOffset selectedAtUtc);
```

Validate expected version, editable state, duplicates, safe revision length, and UTC timestamps before changing state. Increment the version once, add every accepted row at that new version, copy current content, and recreate critical fields as unresolved. Update `UpdateTypedContent` and `SetApprovedMessage` to carry the prior recipient snapshot forward while still invalidating critical and final confirmations.

- [ ] **Step 4: Require an approved message for final confirmation**

Change `ConfirmForDispatch` to reject an absent approved message and keep its existing checks for `PendingConfirmation`, current recipients, current exact-version critical confirmations, active same-organization practitioners, and expected version.

- [ ] **Step 5: Run domain tests**

Run: `dotnet test tests/CriticalAlerts.Domain.Tests/CriticalAlerts.Domain.Tests.csproj -c Release`

Expected: PASS.

- [ ] **Step 6: Commit the domain slice**

```powershell
git add src/backend/CriticalAlerts.Domain/Alerts tests/CriticalAlerts.Domain.Tests/AlertStateMachineTests.cs
git commit -m "feat: version phase 6 recipient snapshots"
```

## Task 2: Persist versioned recipient evidence in PostgreSQL

**Files:**

- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/` (the two files generated by the named EF migration command)
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/CriticalAlertsDbContextModelSnapshot.cs`
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs`

- [ ] **Step 1: Write failing PostgreSQL tests**

Cover two versions of the same practitioner/channel, rejection of a duplicate within one version, persistence of directory revision/source/on-call snapshot, and organization-scoped foreign keys.

```csharp
await SaveSelectionAsync(alertId, version: 2, practitionerId, NotificationChannel.SecureMessage);
await SaveSelectionAsync(alertId, version: 3, practitionerId, NotificationChannel.SecureMessage);
(await db.AlertRecipientSelections.CountAsync()).Should().Be(2);
```

- [ ] **Step 2: Run the focused infrastructure tests and confirm failure**

Run: `dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj -c Release --filter PersistenceFoundationTests`

Expected: FAIL because the existing unique index omits `alert_version` and snapshot columns do not exist.

- [ ] **Step 3: Update the mapping and generate the migration**

Map `directory_revision` (128), `directory_source_updated_at_utc`, and `on_call_snapshot` (80). Replace the unique index with:

```csharp
builder.HasIndex(entity => new
    {
        entity.AlertId,
        entity.AlertVersion,
        entity.PractitionerId,
        entity.Channel,
    })
    .IsUnique()
    .HasDatabaseName("UX_alert_recipient_selection_version_practitioner_channel");
```

Run: `dotnet ef migrations add Phase6RecipientSnapshots --project src/backend/CriticalAlerts.Infrastructure --startup-project src/backend/CriticalAlerts.Api`

- [ ] **Step 4: Inspect the migration**

Confirm it drops only the old recipient index, adds the three safe snapshot columns, creates the versioned index, and introduces no endpoint, patient, clinical, provider, delivery, or worker schema.

- [ ] **Step 5: Run PostgreSQL tests**

Run: `dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj -c Release`

Expected: PASS against PostgreSQL 18 Testcontainers.

- [ ] **Step 6: Commit the persistence slice**

```powershell
git add src/backend/CriticalAlerts.Infrastructure/Persistence tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs
git commit -m "feat: persist recipient selection evidence"
```

## Task 3: Add safe directory selection metadata and validation

**Files:**

- Modify: `src/backend/CriticalAlerts.Application/Directory/DirectoryContracts.cs`
- Create: `src/backend/CriticalAlerts.Application/Directory/DirectorySelectionRevision.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectorySearchService.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectorySelectionResolver.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/DirectoryEndpoints.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/DirectoryImportAndSearchTests.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/DirectoryAuthorizationAndImportTests.cs`

- [ ] **Step 1: Write failing search and resolver tests**

Test department/site/on-call filters, available channel kinds without endpoint values, stable revisions, changed revision conflicts, inactive and cross-organization rejection, role ownership, and similar-name disambiguation.

- [ ] **Step 2: Extend the safe contracts**

```csharp
public sealed record DirectorySearchQuery(
    OrganizationId OrganizationId,
    string? Text,
    string? Department,
    string? Site,
    bool? OnCallNow,
    bool IncludeInactive);

public sealed record DirectorySelectionCandidate(
    PractitionerId PractitionerId,
    PractitionerRoleId? PractitionerRoleId,
    NotificationChannel Channel,
    string PresentedRevision);
```

Add `PractitionerRoleId`, `AvailableChannels`, and `SelectionRevision` to `DirectoryPractitionerListItem`. The revision utility must hash a canonical UTF-8 representation with SHA-256 and Base64URL output; it must never accept endpoint values.

- [ ] **Step 3: Implement one resolver boundary**

Define `IDirectorySelectionResolver.ResolveAsync(OrganizationId, IReadOnlyCollection<DirectorySelectionCandidate>, DateTimeOffset, CancellationToken)` returning `ValidatedRecipientSelection` values. Query all practitioners, roles, current on-call rows, safe source timestamps, and active endpoint kinds in one organization-scoped operation. Validate the full set before returning.

- [ ] **Step 4: Extend the search endpoint**

Accept `department`, `site`, and `onCallNow` query parameters. Preserve `includeInactive` for directory administration, but the recipients UI requests `includeInactive=false`. Do not accept `organizationId`.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj -c Release --filter DirectoryImportAndSearchTests`

Run: `dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj -c Release --filter DirectoryAuthorizationAndImportTests`

Expected: PASS.

- [ ] **Step 6: Commit the directory slice**

```powershell
git add src/backend/CriticalAlerts.Application/Directory src/backend/CriticalAlerts.Infrastructure/Directory src/backend/CriticalAlerts.Api/Http/DirectoryEndpoints.cs src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs tests
git commit -m "feat: validate fictional recipient selections"
```

## Task 4: Add approved-message and full recipient commands

**Files:**

- Modify: `src/backend/CriticalAlerts.Application/Alerts/AlertDraftContracts.cs`
- Create: `src/backend/CriticalAlerts.Application/Alerts/AlertReviewContracts.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertDraftService.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/AlertDraftAuthorizationAndConcurrencyTests.cs`

- [ ] **Step 1: Write failing API tests**

Cover Operator and Administrator success, Practitioner/anonymous denial, foreign-organization not-found, ignored client `organizationId`, stale expected version, full-set replacement, clear, duplicate, inactive, unavailable channel, changed directory revision, protected approved-message storage, and sanitized errors.

- [ ] **Step 2: Add exact command contracts**

```csharp
public sealed record SetApprovedMessageRequest(int ExpectedVersion, string? ApprovedMessage);

public sealed record AlertRecipientInput(
    Guid PractitionerId,
    Guid? PractitionerRoleId,
    string? Channel,
    string? DirectoryRevision);

public sealed record ReplaceAlertRecipientsRequest(
    int ExpectedVersion,
    IReadOnlyList<AlertRecipientInput>? Recipients);
```

Add `SetApprovedMessageAsync` and `ReplaceRecipientsAsync` to the application service boundary. Protect approved content with purpose `alert-approved-message`. Parse channel names using an explicit allowlist and pass candidates through `IDirectorySelectionResolver` before mutating the alert.

- [ ] **Step 3: Map the endpoints**

```csharp
alerts.MapPut("/{alertId:guid}/approved-message", SetApprovedMessage);
alerts.MapPut("/{alertId:guid}/recipients", ReplaceRecipients);
```

Use the existing `AlertDraftEditor` policy and server-derived actor context. Map stale draft or directory revisions to HTTP 409. Never echo input strings in RFC 7807 details.

- [ ] **Step 4: Preserve recipient snapshots across content edits**

Include `RecipientSelections` in alert loads. Verify that typed/SBAR, critical values/units, approved message, and recipient edits each increment `DraftVersion`; typed/SBAR and approved-message edits copy the previous recipient snapshot, while recipient replacement writes exactly the submitted set. All such edits recreate critical fields unresolved for the new version.

- [ ] **Step 5: Run the focused API suite**

Run: `dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj -c Release --filter AlertDraftAuthorizationAndConcurrencyTests`

Expected: PASS against PostgreSQL 18 Testcontainers.

- [ ] **Step 6: Commit the command slice**

```powershell
git add src/backend/CriticalAlerts.Application/Alerts src/backend/CriticalAlerts.Infrastructure/Alerts src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs tests/CriticalAlerts.Api.IntegrationTests/AlertDraftAuthorizationAndConcurrencyTests.cs
git commit -m "feat: add versioned recipient commands"
```

## Task 5: Build the exact review projection

**Files:**

- Modify: `src/backend/CriticalAlerts.Application/Alerts/AlertReviewContracts.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`
- Create: `tests/CriticalAlerts.Api.IntegrationTests/AlertReviewTests.cs`

- [ ] **Step 1: Write failing exact-review tests**

Test every displayed field, exact version/value/unit confirmation evidence, selected recipient/channel snapshots, directory timestamps/on-call labels, `DEMO` policy versions, protected-value decryption only in the authorized response, and conflicts for Draft, incomplete, stale, or changed versions.

- [ ] **Step 2: Define the review response**

```csharp
public sealed record AlertReviewView(
    Guid AlertId,
    int DraftVersion,
    string State,
    string SimulationPatientReference,
    string Location,
    string UrgencyLabel,
    string ApprovedMessage,
    IReadOnlyList<AlertReviewCriticalField> CriticalFields,
    IReadOnlyList<AlertReviewRecipient> Recipients,
    string DemoEscalationPolicyVersion,
    string DemoNotificationPolicyVersion);
```

Recipient views contain display name, specialty, department, site, role title, channel, selected-at, safe source timestamp, on-call snapshot, stale flag, and revision. They never contain endpoint values.

- [ ] **Step 3: Implement and map the query**

Add `GET /api/alerts/{alertId}/review`. Load by authenticated organization and verify `PendingConfirmation`, exact current confirmations, non-empty approved message, and at least one current recipient. Use HTTP 409 for a reload-required state and 404 for a foreign organization.

- [ ] **Step 4: Add sensitive-data sentinels**

Capture API logs and rejected RFC 7807 responses while using distinct synthetic sentinels for patient reference, source, every SBAR field, approved message, and endpoint value. Assert only the explicitly authorized review response contains the approved message and synthetic patient reference.

- [ ] **Step 5: Run review tests**

Run: `dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj -c Release --filter AlertReviewTests`

Expected: PASS.

- [ ] **Step 6: Commit the review slice**

```powershell
git add src/backend/CriticalAlerts.Application/Alerts src/backend/CriticalAlerts.Infrastructure/Alerts src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs tests/CriticalAlerts.Api.IntegrationTests/AlertReviewTests.cs
git commit -m "feat: expose exact alert review"
```

## Task 6: Confirm idempotently and create the outbox item atomically

**Files:**

- Modify: `src/backend/CriticalAlerts.Application/Alerts/AlertReviewContracts.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`
- Create: `tests/CriticalAlerts.Api.IntegrationTests/AlertConfirmationTests.cs`
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs`

- [ ] **Step 1: Write failing confirmation tests**

Cover missing/invalid key, successful exact-version confirmation, duplicate same-key replay, same-key/different-version conflict, two simultaneous requests, stale version, missing approved message, changed recipients, inactive practitioner, rollback on outbox failure, exactly one audit/outbox/state transition, identifier-only payload, and no provider/delivery behavior.

```csharp
body.RootElement.GetProperty("alertId").GetGuid().Should().Be(alertId);
body.RootElement.GetProperty("draftVersion").GetInt32().Should().Be(expectedVersion);
body.RootElement.EnumerateObject().Select(item => item.Name)
    .Should().BeEquivalentTo("alertId", "draftVersion");
```

- [ ] **Step 2: Add confirmation contracts**

```csharp
public sealed record ConfirmAlertReviewRequest(int ExpectedVersion);

public sealed record ConfirmAlertReviewResult(
    Guid AlertId,
    int ConfirmedVersion,
    string State,
    bool Replayed);
```

Canonical request hash input is `confirm-review|{organizationId:D}|{alertId:D}|{expectedVersion}`. Bound `Idempotency-Key` to 1-128 visible ASCII characters before storage.

- [ ] **Step 3: Implement one PostgreSQL transaction**

Inside a transaction, load the completed idempotency record first. Replay only when its hash matches. Otherwise validate the exact review, call `ConfirmForDispatch`, append `alert.confirmed`, create and complete the idempotency record, and add:

```csharp
var payload = JsonSerializer.Serialize(new
{
    alertId = alert.Id.Value,
    draftVersion = alert.DraftVersion.Value,
});
var outbox = OutboxMessage.Create(
    OutboxMessageId.New(),
    organizationId,
    "AlertDispatchRequested",
    alert.Id.Value,
    payload,
    $"alert-dispatch:{alert.Id.Value:D}:v{alert.DraftVersion.Value}",
    now);
```

Save state transition, audit, idempotency, and outbox together, then commit. Resolve unique-key races by reloading the completed record and replaying only an identical hash. Do not invoke `CriticalAlerts.Worker` or any provider interface.

- [ ] **Step 4: Map deliberate confirmation**

Add `POST /api/alerts/{alertId}/confirm`, require the header, derive actor/organization from claims, and return HTTP 200 for both initial success and identical replay. Return HTTP 409 for hash/version conflicts and safe RFC 7807 responses for every rejection.

- [ ] **Step 5: Prove non-disclosure and no dispatch processing**

Scan captured logs, audit metadata, idempotency fields, and outbox JSON for the synthetic patient/source/SBAR/approved-message/practitioner/endpoint sentinels. Assert `DeliveryAttempts` remains empty and no provider double is called.

- [ ] **Step 6: Run confirmation and persistence tests**

Run: `dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj -c Release --filter AlertConfirmationTests`

Run: `dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj -c Release --filter PersistenceFoundationTests`

Expected: PASS.

- [ ] **Step 7: Commit the confirmation slice**

```powershell
git add src/backend/CriticalAlerts.Application/Alerts src/backend/CriticalAlerts.Infrastructure/Alerts src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs tests/CriticalAlerts.Api.IntegrationTests/AlertConfirmationTests.cs tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs
git commit -m "feat: confirm reviewed alerts idempotently"
```

## Task 7: Build compose, recipients, and review pages

**Files:**

- Modify: `src/web/app/alerts/new/page.tsx`
- Create: `src/web/app/alerts/[id]/compose/page.tsx`
- Create: `src/web/app/alerts/[id]/recipients/page.tsx`
- Create: `src/web/app/alerts/[id]/review/page.tsx`
- Create: `src/web/lib/alerts.ts`
- Modify: `src/web/tests/alert-compose.test.tsx`
- Create: `src/web/tests/alert-recipients.test.tsx`
- Create: `src/web/tests/alert-review.test.tsx`
- Modify: `src/web/e2e/smoke.spec.ts`

- [ ] **Step 1: Write failing component tests**

Test redirect from create to dynamic compose, approved-message editing, visible version, search filters, similar-name metadata, inactive exclusion, stale warnings, manual channel selection, selected summary, full-set save, conflict reload guidance, exact review fields, deliberate checkbox, disabled double-submit, replay success, and absence of auto-selection/live/delivery claims.

- [ ] **Step 2: Add typed API helpers**

Centralize fetch, RFC 7807 parsing, and generated idempotency keys in `src/web/lib/alerts.ts`. The confirmation helper accepts the exact review version and retains one key across retries of the same user action.

```typescript
export type ConfirmResult = {
  alertId: string;
  confirmedVersion: number;
  state: "DispatchQueued";
  replayed: boolean;
};
```

- [ ] **Step 3: Split the compose route**

After draft creation, navigate to `/alerts/{id}/compose`. The dynamic compose page loads the protected draft, edits source/SBAR/critical fields, stores the separate approved message, and links to recipients. After recipient replacement, it accepts the return navigation, explains that the version changed, requires critical-field reconfirmation for that version, submits for confirmation, and then navigates to review.

- [ ] **Step 4: Build manual recipients**

Render name/specialty/department/site/on-call search and safe directory timestamps. Use unchecked controls by default. Let the operator select practitioner/channel pairs, show a complete summary, and send one replacement request with all presented revisions. After save, return to compose for mandatory critical-field reconfirmation of the new version. Never show endpoint values or recommend a recipient.

- [ ] **Step 5: Build exact review and deliberate confirmation**

Render the exact review response without reassembling it from draft/search calls. Require an unchecked acknowledgement, display `Draft version {n}`, label the button `Confirm and queue simulation alert`, disable during the request, and state only that the alert was queued for future simulation dispatch.

- [ ] **Step 6: Run web checks**

Run: `npm --prefix src/web test`

Run: `npm --prefix src/web run typecheck`

Run: `npm --prefix src/web run lint`

Expected: PASS.

- [ ] **Step 7: Run Playwright smoke**

Run: `npm --prefix src/web run test:e2e`

Expected: PASS through create, compose, recipients, review, one confirmation, and identical double-submit protection using fictional data.

- [ ] **Step 8: Commit the web slice**

```powershell
git add src/web
git commit -m "feat: add phase 6 review flow"
```

## Task 8: Update documentation and close the Phase 6 gate

**Files:**

- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `docs/product/workflow.md`
- Modify: `docs/product/definition-of-done.md`
- Modify: `docs/architecture/recipient-selection-and-review.md`
- Modify: `docs/security/logging-policy.md`
- Modify: `docs/security/threat-model.md`

- [ ] **Step 1: Document implemented behavior and decisions**

Mark only verified checklist items. State explicitly that source, SBAR, and approved content stay separate; stale-active selection is a simulation assumption; production eligibility/freshness/channels/confirmer roles are `REQUIRES_HOSPITAL_DECISION`; confirmation creates but does not process an identifier-only outbox item; and no provider can run through Phase 6.

- [ ] **Step 2: Run changed-file formatting**

Run: `dotnet format CriticalAlerts.sln --verify-no-changes --include <changed-csharp-files>`

Run: `git diff --check`

Expected: PASS. If the repository-wide formatting baseline still reports untouched-file differences, record it separately and prove changed C# files are clean.

- [ ] **Step 3: Run the full verification suite**

Run: `./scripts/test-all.ps1`

Run: `./scripts/verify-no-sensitive-data.ps1`

Expected: sensitive-data scan, Release build, all backend tests, all web tests, typecheck, lint, and Playwright pass with Docker running.

- [ ] **Step 4: Verify the phase boundary**

Run: `git diff phase-5...HEAD -- src/backend/CriticalAlerts.Worker`

Expected: no output.

Run: `rg -n "provider|DeliveryAttempt|lease|retry|callback|acknowledg|responsibility" src/backend/CriticalAlerts.Application/Alerts src/backend/CriticalAlerts.Infrastructure/Alerts src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`

Expected: no provider invocation, delivery processing, retry, callback, acknowledgement, or responsibility-acceptance implementation. Documentation and test names may describe their absence.

- [ ] **Step 5: Verify from a clean clone**

Clone the reviewed commit into a temporary directory outside the active checkout, create an ignored fictional `.env`, and run `./scripts/test-all.ps1`. Remove the verified temporary clone using one PowerShell path after confirming its resolved absolute path is inside the chosen temporary parent.

Expected: PASS from the reviewed commit without untracked source files.

- [ ] **Step 6: Prepare the required handoff and stop**

Report files changed, decisions made, commands run, test results, limitations, human actions, and proposed commit message `feat: add manual recipient selection and exact alert review`. Do not tag `phase-6`, merge, push, or begin Phase 7 until the project owner approves.

---

## Required review evidence

The Phase 6 handoff must include explicit proof that:

1. every content, unit, approved-message, or recipient edit increments `DraftVersion` and invalidates older confirmations;
2. confirmations are tied to exact value, unit, version, recipients, channels, message, and policy versions;
3. original source is preserved separately from SBAR and approved message;
4. negative authorization and organization isolation pass;
5. logs, errors, audit, idempotency, and outbox contain no complete clinical/patient/contact payload;
6. double confirmation is safe under sequential and concurrent requests;
7. the confirmation transaction cannot leave `DispatchQueued` without its audit, idempotency result, and identifier-only outbox item; and
8. no Phase 6 endpoint processes dispatch or calls a provider.
