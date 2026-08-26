# Phase 4 Review Corrections and Phase 5 Alert Drafting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Work test-first: add a focused failing regression test, run it red, implement the smallest safe change, then run it green before moving on.

**Goal:** Correct the verified Phase 4 directory-boundary gaps and begin the Phase 5 simulation-only alert-drafting slice without entering recipient selection, dispatch, or any real hospital integration.

**Architecture:** Keep `IDirectorySourceAdapter` as the source-specific boundary. CSV remains one adapter over normalized directory records; child role, endpoint, and on-call rows are source-owned and reconciled only for the importing source. Keep organization, user, and role authority in authenticated server context. Add alert drafting through the existing domain aggregate, protected-value abstraction, application command service, PostgreSQL persistence, API, and a simulation compose page.

**Tech Stack:** C# 14, .NET 10, ASP.NET Core minimal APIs, EF Core/Npgsql, PostgreSQL/Testcontainers, xUnit/FluentAssertions, TypeScript, React, Next.js App Router, Vitest/Testing Library, Playwright, PowerShell.

**Spec:** `C:\Users\ghait\Downloads\hospital-critical-alert-platform-master-build-plan.md`, Phase 4 and Phase 5 sections; existing Phase 4 design and closure evidence remain the boundary baseline. The latest user request explicitly supersedes the repository Phase 4 stop instruction for this turn only.

## Global Constraints

- Use fictional `SIM-` identifiers, synthetic names, simulation-only clinical text, and `555` contact values. Do not add PHI, real employee/contact data, credentials, tokens, provider calls, hospital connections, SCIM, Graph, FHIR, Entra, AI, speech, SMS/voice dispatch, outbox workers, recipient selection, or Phase 6/7 behavior.
- Preserve server-derived organization/user/role authority. Never trust request headers, form fields, or JSON fields for authentication, role elevation, or organization scope.
- Keep sensitive contact endpoints, alert source text, and SBAR content behind `ISensitiveDataProtector`; API responses return only authorized simulation content and never ciphertext or raw contact endpoint values.
- Use UTC instants and optimistic draft-version checks. Use safe problem details and correlation IDs; do not reveal user/role configuration or exception internals.
- Directory import must be preview-first, source-scoped, organization-scoped, and transactional. A failed row or stale preview must not partially mutate the database.
- Phase 5 ends at editable drafts, typed source, SBAR content, critical-field confirmation, and draft submission for confirmation. Do not add recipient APIs, review/dispatch confirmation, idempotency for dispatch, provider behavior, or a `phase-5` completion tag.
- Update documentation and tests with behavior changes. Retain `REQUIRES_HOSPITAL_DECISION` for production source-of-truth, stale-selection, freshness-window, merge, and clinical-policy decisions.

## Task 1: Add red regression coverage for the Phase 4 review findings

**Files:**

- Modify: `tests/CriticalAlerts.Application.Tests/CsvDirectoryParserTests.cs`
- Modify: `tests/CriticalAlerts.Domain.Tests/` directory/on-call tests or add a focused test file
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/DirectoryImportAndSearchTests.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/DirectoryAuthorizationAndImportTests.cs`
- Modify: `src/web/tests/page.test.tsx` only for changed import-token behavior

Add tests before production changes for:

1. Invalid role titles are blocking parser errors and no arbitrary role value reaches persistence.
2. On-call end timestamps must be after start timestamps; expired and future assignments are not current search results, while a current assignment retains explicit Primary/Backup tier.
3. On-call site/department pairs must belong together.
4. Applying a CSV replaces only rows owned by `SIM-CSV`; a second source's role, protected endpoint, and on-call rows remain. Reapplying CSV remains idempotent.
5. A cross-organization import cannot read or mutate the seeded organization.
6. A deliberately injected persistence failure after a practitioner mutation rolls back practitioner, source record, child rows, sync run, and audit event.
7. Exact duplicate physical CSV rows are deduplicated in normalized children rather than creating duplicate practitioners/children.
8. A preview returns a server-generated freshness token; apply without it, with a different file, or after the source catalog changes is rejected before a sync run. Apply with the matching preview token succeeds.
9. API tests continue to prove unauthenticated `401`, Practitioner/Operator import denial, Administrator-only import, authenticated organization scoping, and safe problem details. Add a role-negative alert placeholder only after the Phase 5 endpoint exists.
10. Web import sends the preview token and disables apply when the selected file or preview token changes.

Run the focused application, domain, infrastructure, API, and web tests. The new assertions are expected to fail against the current implementation; record the exact red failures before changing production code.

## Task 2: Correct Phase 4 validation, source ownership, preview freshness, and search

**Files:**

- Modify: `src/backend/CriticalAlerts.Application/Directory/DirectoryContracts.cs`
- Modify: `src/backend/CriticalAlerts.Application/Directory/CsvDirectoryParser.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Directory/PractitionerRoleAssignment.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Directory/ContactEndpoint.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Directory/OnCallAssignment.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectoryImportService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectorySearchService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/DirectoryConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs`
- Add: EF migration and model snapshot for source ownership columns
- Modify: `src/backend/CriticalAlerts.Api/Http/DirectoryEndpoints.cs`
- Modify: `src/web/app/directory/import/page.tsx`

Implement the smallest changes that make the new tests green:

- Add an explicit simulation directory role catalog and reject unknown `role_title` values with a safe `invalid-role` issue. Keep role names visibly fictional and document that this Phase 4 allowlist is a simulation catalog, not a canonical hospital role model.
- Validate on-call windows in both parser and domain (`ends_at_utc > starts_at_utc`), keep timestamps normalized to UTC, and validate site/department ownership for on-call rows as well as roles.
- Add `SourceSystem` and `SourceRecordId` to practitioner roles and contact endpoints, with source metadata on creation and a migration-safe legacy default. Reconciliation removes/replaces only the importing source’s child rows. On-call rows already carry source ownership; apply the same source filter there. Seed the existing fictional baseline with explicit `SIM-DIRECTORY` ownership.
- Keep practitioner matching limited to `(organization, source_system, source_record_id)` then `(organization, simulation_code)`; never use names. Keep duplicate source rows and same-name collisions separate and visible.
- Add a deterministic, non-secret preview token derived from organization, adapter source, uploaded payload hash, and the scoped directory catalog revision. Return it in preview results and require it on apply; reject missing/mismatched tokens before opening a transaction. Re-plan current bytes and current catalog at apply time so a changed file or changed source mapping cannot reuse an old preview.
- Keep apply transactional, including sync run and audit event. Add a test-only failure seam or injected persistence collaborator rather than production-only test behavior; no partial mutation is acceptable.
- Inject `TimeProvider` into directory search and return only assignments where `starts_at_utc <= now < ends_at_utc`. On-call status remains separate from `Practitioner.IsActive`; inactive practitioners remain searchable but `Selectable` stays false.
- Derive the organization/user in the API from claims as today. Read the preview token from the multipart form only as a freshness value, never as identity or authorization. Update the UI to carry the token and clear it when the file changes.

Run the focused Phase 4 suites, then the full backend and web checks. Do not create or move the existing `phase-4` tag as part of this follow-up.

## Task 3: Add Phase 5 alert draft domain behavior with red/green tests

**Files:**

- Modify: `src/backend/CriticalAlerts.Domain/Alerts/Alert.cs`
- Modify: `tests/CriticalAlerts.Domain.Tests/AlertStateMachineTests.cs`
- Add: `src/backend/CriticalAlerts.Application/Alerts/AlertDraftContracts.cs`
- Add: `tests/CriticalAlerts.Application.Tests/AlertDraftServiceTests.cs` if application tests are appropriate

Extend the existing aggregate without creating a dispatch path:

- Add a domain operation for replacing protected structured SBAR content with an expected `AlertDraftVersion`; edits increment the version, invalidate prior confirmation/approval state, and return PendingConfirmation alerts to Draft.
- Add a domain completeness check used by `SubmitForConfirmation`: typed source, nonblank location, nonblank urgency, and required SBAR fields must be present; unresolved critical-field confirmations must block progression. Preserve the existing rule that every critical number/unit remains unresolved until a human explicitly confirms it.
- Keep `AlertSourceType.Typed`; do not add dictated/transcription behavior. Require synthetic patient references and simulation-only content as already enforced by the domain policy.
- Define application request/response records for create, edit, fetch, critical-field confirmation, and submit. Responses expose safe typed DTOs and current version/state, never `ProtectedValue` internals.
- Add tests for create Draft, typed/SBAR edit increments, stale edit rejection, required-field rejection, unresolved-field rejection, confirmed-field acceptance, and cross-organization protection at the service boundary.

Run the domain/application test filters and keep the existing Phase 0/2 alert state-machine tests green. Do not add recipient selection or dispatch assertions to the new API.

## Task 4: Implement the Phase 5 application service and API boundary

**Files:**

- Add: `src/backend/CriticalAlerts.Application/Alerts/IAlertDraftService.cs`
- Add: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertDraftService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Add: `src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`
- Modify: `src/backend/CriticalAlerts.Api/Program.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/` with `AlertDraftAuthorizationAndConcurrencyTests.cs`

Implement a scoped service that loads alerts only by authenticated organization and alert ID, unprotects content only for the authorized simulation actor, protects all writes with explicit purposes, and saves aggregate transitions atomically. Handle `StaleAlertVersionException`, domain completeness failures, missing alerts, cross-organization IDs, and EF concurrency as safe problem details.

Map only these existing-style `/api/alerts` routes:

- `POST /api/alerts/drafts` — Operator or Administrator; create a typed draft using authenticated organization/user and server-validated site/department IDs.
- `GET /api/alerts/{alertId}` — Operator or Administrator; organization-scoped safe draft read.
- `PATCH /api/alerts/{alertId}` — Operator or Administrator; expected-version edit of typed source/SBAR/location/urgency with no client-supplied organization/user/role.
- `POST /api/alerts/{alertId}/field-confirmations` — Operator or Administrator; expected-version human confirmation of a critical field.
- `POST /api/alerts/{alertId}/submit-for-confirmation` — Operator or Administrator; required fields and current confirmations are checked server-side.

Do not map recipient, review, confirmation-for-dispatch, delivery, provider, outbox, or escalation endpoints. Add negative API tests for unauthenticated, Practitioner, cross-organization, stale-version, and incomplete-draft requests; add positive Operator and Administrator tests. Assert response bodies do not contain ciphertext, protected-value property names, raw contact endpoints, or role configuration details.

Reuse the existing `structured_suggestion` protected column for a typed serialized SBAR record so no Phase 5 schema is invented beyond the approved model. If EF migration is needed for Phase 4 ownership only, generate it with the pinned SDK and verify the model snapshot.

## Task 5: Add the simulation compose UI and Phase 5 documentation

**Files:**

- Add: `src/web/app/alerts/new/page.tsx` or the smallest compose route consistent with the existing App Router shell
- Modify: `src/web/app/simulation-chrome.tsx`
- Modify: `src/web/tests/page.test.tsx` or add `src/web/tests/alert-compose.test.tsx`
- Modify: `README.md` and/or `docs/architecture/` Phase 5 status documentation

Build a clearly simulation-only compose form with typed source, synthetic patient reference, location, urgency, Situation, Background, Assessment, Recommendation, version display, save/update status, critical-field confirmation controls, and a submit-for-confirmation action. Render server errors without exposing implementation details. Show stale-version recovery guidance and never render recipient or dispatch controls. Keep the development identity switcher constrained by the existing backend auth boundary.

Add web tests for required-field feedback, successful create/edit payloads, version display, stale-version handling, critical-field confirmation, and absence of dispatch/recipient controls. Update navigation and the phase boundary copy to state that Phase 5 drafting is available while recipient selection and dispatch remain unavailable.

## Task 6: Verify, review, and hand off without premature completion claims

Run, with the pinned local toolchain and project PostgreSQL/Testcontainers infrastructure:

```powershell
.\scripts\test-all.ps1
```

Also run the focused red/green commands for parser, domain, application, infrastructure, API, and web tests, a fresh clone migrate/reset-demo/Phase 4+5 smoke check, repository safety scan, `git diff --check`, and a review of changed files for secrets, generated artifacts, unrelated Phase 6/7 code, and real-world data.

Before any remote mutation, verify `git status`, branch tracking, and ahead/behind state. Commit Phase 4 corrections and the Phase 5 start as a clear local commit (or focused commits if the diff is easier to review). Push only if the tracked remote has not advanced and the user’s current authorization still covers the ordinary repository handoff; never force-push. Do not create a `phase-5` tag because this task starts Phase 5 but does not satisfy its final gate. Report exact tests, commits, push outcome, and any blocked environment check.
