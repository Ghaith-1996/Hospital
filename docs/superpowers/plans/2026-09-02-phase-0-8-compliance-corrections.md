# Phase 0-8 Compliance Corrections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the actionable gaps identified in the user-provided Phase 0-8 audit while preserving simulation-only safety boundaries and leaving external GitHub administration for a human owner.

**Architecture:** Keep the modular monolith and transactional PostgreSQL model. Add protected patient-reference and immutable source-revision records at the domain/persistence boundary, expose closed-loop lifecycle commands through an organization-scoped application service, and apply API hardening centrally at the ASP.NET endpoint boundary. Keep all new workflow behavior deterministic and explicitly simulation-only.

**Tech Stack:** C# 14, ASP.NET Core/.NET 10, EF Core 10, PostgreSQL 18, Npgsql 10, TypeScript, React, Next.js App Router, PowerShell, GitHub Actions, Docker.

**Spec:** User-provided audit pasted in `C:\Users\ghait\.codex\attachments\52c83752-d562-4e4c-9acf-aaf26d3ded56\pasted-text.txt`, constrained by `AGENTS.md` and the product/architecture/security documents under `docs/`.

## Execution status — 2026-09-02

The implementation portions of Tasks 1–7 are applied in the working tree. Local formatting, locked restore, Debug/Release builds, non-Docker regression tests, web tests/typecheck/lint/build, OpenAPI verification, dependency scans, sensitive-data scanning, script parsing, and diff checks pass. PostgreSQL/Testcontainers verification, container image builds, and the full release runner remain pending because Docker Desktop is unavailable on this host; the Playwright smoke runner also did not complete locally. Human approval and GitHub branch/ruleset administration remain external gates.

## Global Constraints

- Fictional hospital, practitioner, patient, endpoint, and clinical data only.
- Protected workflow content, including patient references and source revisions, never appears in logs, URLs, outbox payloads, or raw API storage shapes.
- Dispatch still requires exact-version authenticated human confirmation and an identifier-only transactional outbox record.
- New response, resolution, cancellation, fallback, and selection-source behavior is simulation-only and must not invent production hospital policy; unresolved policy remains `REQUIRES_HOSPITAL_DECISION`.
- Every production behavior change has a failing regression test before implementation and fresh verification before completion.

---

### Task 1: Protect patient references and preserve source revisions

**Files:**
- Create: `src/backend/CriticalAlerts.Domain/Alerts/AlertSourceRevision.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Alerts/Alert.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertDraftService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientInboxService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/CriticalAlertsDbContextModelSnapshot.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/<timestamp>_ProtectedPatientReferencesAndSourceRevisions.cs`
- Create: `tests/CriticalAlerts.Domain.Tests/AlertSourceRevisionTests.cs`
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/AlertDraftAuthorizationAndConcurrencyTests.cs`

**Interfaces:**
- Consumes: `ISensitiveDataProtector`, `SimulationEnvironmentPolicy`, current `Alert` draft versioning, and current `ProtectedValue` mapping.
- Produces: `Alert.SimulationPatientReference` as protected storage, an append-only `AlertSourceRevision` collection keyed by alert/version, and views that decrypt only through the application service.

- [ ] **Step 1: Write failing domain and persistence tests**

  Assert that an alert requires a protected patient-reference value, source edits keep the initial source and append a new immutable revision for the new draft version, and PostgreSQL stores ciphertext columns rather than a plaintext `simulation_patient_reference` column.

- [ ] **Step 2: Run the focused tests and verify the expected failures**

  Run `dotnet test tests/CriticalAlerts.Domain.Tests/CriticalAlerts.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~AlertSourceRevision` and the focused persistence test once the pinned SDK is available. Expected failure: the revision entity and protected mapping are absent.

- [ ] **Step 3: Implement the protected value and revision model**

  Add the source-revision entity, preserve the first `OriginalSource`, append revisions after each version increment, load revisions in draft reads, protect patient references with purpose `alert-patient-reference`, and decrypt only when constructing an authorized view.

- [ ] **Step 4: Add the migration and backfill path**

  Add protected patient-reference columns and the source-revision table, backfill existing source revision version 1 rows from the old protected source columns, and provide a controlled application migration path for legacy plaintext patient references before dropping that column.

- [ ] **Step 5: Run focused domain, infrastructure, and API tests**

  Run the domain suite, the PostgreSQL persistence suite, and the protected draft API suite. Expected: source revisions, ciphertext-only storage, and API plaintext behavior pass without exposing encryption metadata.

---

### Task 2: Add recipient-selection provenance

**Files:**
- Modify: `src/backend/CriticalAlerts.Domain/Enums.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Alerts/AlertRecipientSelection.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Alerts/Alert.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectorySelectionResolver.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Application/Alerts/AlertDraftContracts.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertDraftService.cs`
- Modify: `src/web/lib/alerts.ts`
- Create: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/<timestamp>_RecipientSelectionProvenance.cs`
- Modify: `tests/CriticalAlerts.Domain.Tests/AlertStateMachineTests.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/AlertReviewTests.cs`

**Interfaces:**
- Consumes: manual directory selection resolution.
- Produces: `RecipientSelectionSource` with `Manual`, `TeamExpansion`, and `EscalationPolicy`, persisted and returned in recipient/review views; the current resolver always emits `Manual`.

- [ ] **Step 1: Add a failing provenance assertion**

  Extend recipient replacement/review tests to assert `selectionSource == "Manual"` and that the domain preserves the source when carrying recipients to a new draft version.

- [ ] **Step 2: Run the focused test and verify it fails**

  Run the relevant domain/API test filters. Expected failure: recipient views have no source field.

- [ ] **Step 3: Implement and migrate the source field**

  Add the enum/property, set it from the resolver, map it as a required string with a safe default for existing rows, and include it in application/frontend contracts.

- [ ] **Step 4: Run recipient, review, and full web checks**

  Run the focused backend tests plus `npm --prefix src/web test -- --run`, `npm --prefix src/web run typecheck`, and `npm --prefix src/web run lint`.

---

### Task 3: Complete simulated response and operator closed-loop actions

**Files:**
- Modify: `src/backend/CriticalAlerts.Domain/Enums.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Delivery/RecipientResponse.cs`
- Modify: `src/backend/CriticalAlerts.Application/Responses/RecipientResponseContracts.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientResponseService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientInboxService.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/RecipientResponseEndpoints.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/AlertLiveEndpoints.cs`
- Create: `src/backend/CriticalAlerts.Application/Alerts/AlertLifecycleContracts.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertLifecycleService.cs`
- Create: `src/backend/CriticalAlerts.Api/Http/AlertLifecycleEndpoints.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Modify: `src/web/lib/alerts.ts`
- Modify: `src/web/app/my-alerts/[id]/page.tsx`
- Modify: `src/web/app/alerts/[id]/live/page.tsx`
- Modify: `src/web/tests/phase8-recipient-inbox.test.tsx`
- Modify: `src/web/tests/phase8-alert-live.test.tsx`
- Create: `tests/CriticalAlerts.Api.IntegrationTests/AlertLifecycleAuthorizationTests.cs`
- Modify: `tests/CriticalAlerts.Domain.Tests/RecipientResponseStateTests.cs`

**Interfaces:**
- Consumes: authenticated practitioner links, exact confirmed alert versions, `Alert.Resolve`/`Alert.Cancel`, assignment records, and existing idempotency/audit conventions.
- Produces: an allowlisted `CallUnitRequested` response, explicit approved simulation decline reason codes, `POST /api/v1/alerts/{id}/resolve`, `POST /api/v1/alerts/{id}/cancel`, and live projection flags/actions.

- [ ] **Step 1: Write failing response/lifecycle tests**

  Test call-unit requests are accepted as a non-terminal response, arbitrary/free-text decline reasons are rejected, resolution is rejected without an active responsibility assignment, resolution succeeds after acceptance, and cancellation is idempotent and organization-scoped.

- [ ] **Step 2: Run the tests and verify the expected failures**

  Run the domain and API filters. Expected failures include the current explicit call-unit exception and missing lifecycle endpoints.

- [ ] **Step 3: Implement response semantics and lifecycle service**

  Add a `CallUnitRequest` category, allowlisted decline codes, exact request hashes including reason code, and a transactional lifecycle service that validates expected version, current state, assignment requirement for resolve, idempotency key, audit metadata, and optimistic concurrency.

- [ ] **Step 4: Implement the practitioner and live UI**

  Add request-call-unit and decline-reason controls, show safe reason/call-unit state, show a `REQUIRES_HOSPITAL_DECISION` manual-fallback placeholder after failure, and add resolve/cancel controls with accessible status messages.

- [ ] **Step 5: Run focused backend and web regression checks**

  Run the response/lifecycle API tests, web unit tests, typecheck, lint, and production build.

---

### Task 4: Harden destructive demo reset

**Files:**
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/DatabaseOperations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/DatabaseCommandHost.cs`
- Modify: `scripts/db-reset-demo.ps1`
- Modify: `tests/CriticalAlerts.Application.Tests/SimulationResponseEnvironmentGuardTests.cs`
- Create: `tests/CriticalAlerts.Infrastructure.Tests/DatabaseOperationsSafetyTests.cs`
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/PersistenceFoundationTests.cs`

**Interfaces:**
- Consumes: Development/Test environment checks and Npgsql connection strings.
- Produces: a required explicit confirmation flag, loopback-host validation, allowlisted local demo database names, and no destructive operation before all guards pass.

- [ ] **Step 1: Write failing guard tests**

  Assert missing confirmation, remote hosts, and non-demo database names are rejected before `EnsureDeletedAsync`; assert the exact local Testcontainers database with confirmation is accepted by validation.

- [ ] **Step 2: Run the tests and verify failure**

  Run the focused infrastructure tests. Expected failure: the current method has no confirmation or connection-target guard.

- [ ] **Step 3: Implement the explicit guard and script flag**

  Add `-ConfirmDemoReset`, pass `--confirm-demo-reset`, parse the connection string with `NpgsqlConnectionStringBuilder`, and check environment, loopback host, and an allowlisted `critical_alerts_dev`, `critical_alerts_test`, or `critical_alerts_demo` database name before deletion.

- [ ] **Step 4: Run the safety tests and script syntax check**

  Run the focused tests and `Get-Command .\scripts\db-reset-demo.ps1`/PowerShell parsing checks without invoking a destructive reset.

---

### Task 5: Restore API versioning, OpenAPI, rate limits, and request limits

**Files:**
- Modify: `src/backend/CriticalAlerts.Api/Program.cs`
- Modify: `src/backend/CriticalAlerts.Api/Http/*.cs`
- Modify: `src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj`
- Modify: `Directory.Packages.props`
- Modify: backend `packages.lock.json` files through locked restore
- Modify: `src/web/lib/alerts.ts`
- Modify: `src/web/app/development-auth-panel.tsx`
- Modify: `src/web/next.config.ts`
- Modify: existing backend/frontend/e2e tests containing API paths
- Create: `docs/api/openapi.json`
- Create: `scripts/verify-openapi.ps1`
- Create: `tests/CriticalAlerts.Api.IntegrationTests/ApiHardeningTests.cs`

**Interfaces:**
- Consumes: current minimal API endpoint groups and Next.js rewrite.
- Produces: `/api/v1/...` application routes, `/openapi/v1.json` with OpenAPI 3.1, a named fixed-window API limiter, and a bounded multipart request body for directory imports.

- [ ] **Step 1: Write failing route and hardening tests**

  Assert `/api/v1` works, `/api` is not the supported route, OpenAPI reports `3.1.x` and required paths, excessive import bodies return 413, and rate-limit rejection returns 429.

- [ ] **Step 2: Run focused tests and verify failure**

  Run the API hardening filter and web tests. Expected failure: current routes are unversioned and no OpenAPI/limiter metadata exists.

- [ ] **Step 3: Implement versioned routing and middleware**

  Move all API groups and frontend calls under `/api/v1`, add OpenAPI registration/document exposure, add `AddRateLimiter`/`UseRateLimiter`, apply the named policy to API groups, and add endpoint/form-size limits for CSV imports.

- [ ] **Step 4: Add the compatibility artifact and verifier**

  Check in an OpenAPI 3.1 JSON contract for supported routes and make `scripts/verify-openapi.ps1` validate the version, required paths, and absence of unversioned application paths. Run it in CI.

- [ ] **Step 5: Run backend/web verification**

  Run all backend tests, web tests, typecheck, lint, and build when the pinned SDK is available.

---

### Task 6: Separate simulation authorization responsibilities

**Files:**
- Modify: `src/backend/CriticalAlerts.Application/Identity/AuthorizationPolicies.cs`
- Modify: `src/backend/CriticalAlerts.Api/Authentication/DevelopmentAuthenticationServiceCollectionExtensions.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs`
- Modify: `docs/product/product-decisions.md`
- Modify: `docs/product/definition-of-done.md`
- Create: `tests/CriticalAlerts.Application.Tests/AuthorizationRoleMatrixTests.cs`

**Interfaces:**
- Consumes: current role claims and policy registration.
- Produces: distinct simulation role/policy names for Physician, ClinicalSupervisor, DirectoryAdministrator, IntegrationAdministrator, Auditor, and SystemAdministrator while retaining explicit compatibility for existing seeded demo identities.

- [ ] **Step 1: Write the role-matrix tests**

  Assert each named role maps only to its intended policy set and that the current Operator/Administrator/Practitioner fixtures remain usable during migration.

- [ ] **Step 2: Run the tests and verify failure**

  Run the application role-matrix filter. Expected failure: the current authorization model defines only three roles.

- [ ] **Step 3: Implement role constants, policies, and seeded assignments**

  Add distinct role constants/policies and least-privilege policy composition; keep all values simulation-only and document production mapping as `REQUIRES_HOSPITAL_DECISION`.

- [ ] **Step 4: Run authorization regression checks**

  Run development-authentication, API authorization, and role-matrix tests.

---

### Task 7: Complete CI, container, and documentation gates

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `package.json`
- Create: `src/backend/CriticalAlerts.Api/Dockerfile`
- Create: `src/backend/CriticalAlerts.Worker/Dockerfile`
- Create: `src/web/Dockerfile`
- Modify: `scripts/test-all.ps1`
- Modify: `docs/product/definition-of-done.md`
- Modify: `docs/security/production-readiness-gates.md`
- Modify: `README.md`
- Modify: `AGENTS.md`

**Interfaces:**
- Consumes: pinned .NET/Node/PostgreSQL toolchain, existing safety scan, and the OpenAPI verifier.
- Produces: format verification, OpenAPI verification, dependency vulnerability checks, API/worker/web container builds, updated phase evidence, and an explicit note that GitHub rulesets/branch protection require repository-admin action.

- [ ] **Step 1: Write CI contract checks**

  Add a repository-level test/script that checks CI contains format, OpenAPI, dependency, and container gates and that each Dockerfile has the expected pinned base image families.

- [ ] **Step 2: Run the checks and verify failure**

  Run the new verifier. Expected failure: current CI has none of the missing gates and no Dockerfiles.

- [ ] **Step 3: Implement CI and container gates**

  Add `dotnet format --verify-no-changes`, OpenAPI verification, `dotnet list package --vulnerable --include-transitive`, `npm audit --audit-level=high`, and explicit Docker builds for API, worker, and web images. Keep credentials and real data out of every build context.

- [ ] **Step 4: Update phase closure evidence conservatively**

  Mark only code-verifiable items complete, record the unavailable .NET/Docker checks accurately, preserve unchecked human-approval gates, document source-revision/patient-reference/provenance/API/reset corrections, and state that GitHub rulesets and classic branch protection cannot be changed from the repository workspace.

- [ ] **Step 5: Run final verification**

  Run `git diff --check`, the sensitive-data scan, all available backend/frontend tests, web typecheck/lint/build, OpenAPI verification, CI contract verification, and Docker builds where the host permits them. Record every unavailable prerequisite instead of treating it as a pass.
