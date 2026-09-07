# Phase 9 — Escalation Implementation Plan

Status: authorized 2026-09-07; Task 1 is documentation only. Specification: [exact confirmed DEMO escalation](../specs/2026-09-07-phase-9-escalation-design.md). Implement one task at a time and commit isolated slices. No Phase 10. Historical Phase 8.5 acceptance records remain unchanged.

## Sources and working rules

Read AGENTS.md, README.md, product workflow/definition of done, architecture state-machine/data-model/recipient-review/simulated-dispatch, security threat-model/logging-policy, Phase 8 spec/plan, Phase 8.5 spec/verification, and this design before functional work. The owner's supplied 2026-09-07 Phase 9 requirements are the current authority. The separate original master build plan was not available in the task attachments or tracked files; repository documents and the supplied Phase 9 source preserve its relevant requirements. Do not claim it was independently read.

All paths below are repository-relative and exact unless explicitly identified as EF-generated. New files are planned targets; list any necessary adjustment and its reason before the slice. Retain all existing safety tests. For each functional slice: write focused failing tests, execute and record expected RED, implement minimally, run focused GREEN plus its full regression project, inspect sensitive-data boundaries, run `git diff --check`, and commit. A compile failure for a missing API can establish the initial RED but does not replace behavioral assertions. Documentation-only Task 1 does not need a contrived failing test.

Use pinned .NET 10.0.100, Node 24.16.0/npm 11.13.0 and PostgreSQL 18.4. Run commands from the repository root. Focus a test class with `dotnet test <project-path> --configuration Release --filter FullyQualifiedName~<ClassName> --nologo`; run the full named project without the filter after GREEN. Build before using `--no-build`. Unit mocks do not replace real PostgreSQL/Testcontainers or connected system proof.

## Task 1 — Specification and safety boundary

Files: create `docs/superpowers/specs/2026-09-07-phase-9-escalation-design.md`, this plan, `docs/architecture/escalation.md`; modify `AGENTS.md`, `README.md`, `docs/product/workflow.md`, `docs/product/definition-of-done.md`.

- [x] Record simulation authority, exact immutable review/confirmation plan, legacy ineligibility, shared alert-then-run locking, exactly-once response-signal consumption, inactive-backup failure without replacement, database time and identifier-only outbox.
- [x] Review documentation diff, local links and safety wording; run `git diff --check` and repository sensitive-data scan. No functional code or migration.
- [x] Commit `docs: design phase 9 escalation`.

## Task 2 — Exact policy and review/confirmation snapshot

Files:

- Modify `src/backend/CriticalAlerts.Domain/Alerts/Alert.cs`, `src/backend/CriticalAlerts.Domain/Policies/Policies.cs`, `src/backend/CriticalAlerts.Domain/Identifiers.cs`.
- Create `src/backend/CriticalAlerts.Domain/Escalation/AlertEscalationPlan.cs`, `src/backend/CriticalAlerts.Domain/Escalation/AlertEscalationRecipientSnapshot.cs`.
- Modify `src/backend/CriticalAlerts.Application/Alerts/AlertReviewContracts.cs`, `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs`, `src/backend/CriticalAlerts.Api/Http/AlertDraftEndpoints.cs`.
- Create `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationPlanResolver.cs`.
- Test `tests/CriticalAlerts.Domain.Tests/AlertEscalationPlanTests.cs`, `tests/CriticalAlerts.Api.IntegrationTests/AlertReviewTests.cs`, `tests/CriticalAlerts.Api.IntegrationTests/AlertConfirmationTests.cs`.

- [x] RED: missing exact policy/version/revision, deterministic canonical plan order, every backup/channel shown, duplicate/cross-org/inactive backup rejection, policy/directory/on-call changes invalidate review, mismatched revision rolls back all side effects, idempotency hash includes revision, no legacy eligibility.
- [x] Implement immutable domain plan and resolver, exact review DTO and confirmation command. Test precommit consistency under directory/policy mutation and successful replay after later changes. Never select from current policy at processing time.
- [x] Keep this a complete runnable vertical slice: also modify `src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/PolicyConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs` and create `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/EscalationConfigurations.cs` for snapshot persistence. Generate a minimal additive `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/<timestamp>_Phase9EscalationSnapshots.cs` plus Designer and model snapshot; record exact generated paths before commit. Run fresh/legacy PostgreSQL snapshot tests and full domain, infrastructure and API regression. Task 4 adds the separate run/event/signal migration; no knowingly failing continuation is committed.
- [x] Commit `feat: bind exact escalation policy snapshots`.

Task 2 execution evidence (2026-09-07): exact snapshot slice implemented; human Phase 9 acceptance remains pending. The fixed synthetic backup is Jules Martin through explicit `DEMO-role:11111111-1111-4111-8111-111111110705`, SecureMessage, DEMO 60-second first step, one attempt. The domain accepts 1�10 maximum attempts, matching the existing simulation worker configuration ceiling; this is a simulation bound, not hospital policy. No worker/scheduler/activation code is included here.

Confirmation takes the existing alert row lock, then stable SHARE table locks over directory/policy evidence through commit. This prevents both updates and inserted phantom evidence; it intentionally trades cross-organization directory-write throughput for simple DEMO correctness. Runtime review/confirmation now includes the exact policy ID/version, steps, future recipient/channel evidence and required plan revision. The connected review request/display and canonical OpenAPI were updated in this slice so the current workflow remains usable. Future snapshots grant no active selection or inbox access. Existing policy configuration already had organization-scoped keys, so no redundant `PolicyConfigurations.cs` change was necessary.

Generated migration: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260907215641_Phase9EscalationSnapshots.cs` and `.Designer.cs`, plus `CriticalAlertsDbContextModelSnapshot.cs`. It adds nullable alert bindings and organization-scoped immutable plan/recipient tables. EF rejects tracked updates/deletes; PostgreSQL triggers reject UPDATE, DELETE and TRUNCATE. The known fictional placeholder seed is upgraded explicitly; historical alerts are not bound or backfilled.

RED: three new PostgreSQL API assertions failed because missing/wrong revisions returned 200 and review had no plan; two domain assertions failed for empty actor/unbounded attempts; one backup-source assertion failed for absent source UTC evidence; one TRUNCATE assertion failed before its immutable-schema trigger; one web assertion failed because the future backup was absent. GREEN: 67 domain + 39 application + 9 architecture + 68 real-PostgreSQL infrastructure + 138 API tests = 321 backend; 29 web tests; web typecheck/lint; canonical runtime OpenAPI; EF no pending model changes; sensitive-data and web-storage checks. Full backend command: `dotnet test src/backend/CriticalAlerts.sln --no-restore --logger 'trx;LogFileName=task2-full-backend.trx'`, with final full API rerun after fixing new test-fixture assumptions. Existing negative assertions were retained. Fresh-schema tests and a retained Active/confirmed legacy migration prove exact snapshots versus explicit legacy ineligibility. Four deterministic PostgreSQL lock-wait cases cover directory/policy writers before validation and after save/before commit.

The local detailed handoff is `.superpowers/sdd/2026-09-07-phase-9-escalation/task-2-report.md`. Full rendered browser/container/system E2E remains in the later phase gate; this slice verified React component interactions and has not claimed full Phase 9 completion.

## Task 3 — Escalation run domain

Files: modify `src/backend/CriticalAlerts.Domain/Delivery/EscalationRun.cs`, `src/backend/CriticalAlerts.Domain/Enums.cs`; create `src/backend/CriticalAlerts.Domain/Escalation/EscalationEvent.cs`, `src/backend/CriticalAlerts.Domain/Escalation/EscalationConsumedSignal.cs`; test `tests/CriticalAlerts.Domain.Tests/EscalationRunTests.cs`.

- [ ] RED exact alert/policy version, illegal/repeated advances, completed/stopped processing, UTC validation, lease ownership, paused-not-due, remaining-delay resume including zero, accepted responsibility versus acknowledgement/call-unit, lifecycle stop, decline/unavailable once per response.
- [ ] Add explicit Schedule/BeginProcessing/Advance/Pause/Resume/Stop/Complete/ReleaseLease behavior and sanitized event/signal records. Infrastructure never sets domain properties directly.
- [ ] Keep existing PostgreSQL behavior green: explicitly ignore new run properties in `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/DeliveryConfigurations.cs` until Task 4 maps them, so EF convention does not query nonexistent columns. GREEN focused/full domain project and full backend regression.
- [ ] Commit `feat: complete escalation run domain`.

## Task 4 — PostgreSQL persistence and additive migration

Files: modify `src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/DeliveryConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/PolicyConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs`; create `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/EscalationConfigurations.cs`; modify `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/CriticalAlertsDbContextModelSnapshot.cs`.

EF generates `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/<timestamp>_Phase9Escalation.cs` and matching `.Designer.cs`; record the exact generated timestamp paths before commit. Never rename/edit old migrations. Tests: create `tests/CriticalAlerts.Infrastructure.Tests/EscalationPersistenceTests.cs`; retain Task 2 snapshot/API integration assertions and extend them for the run schema.

- [ ] RED run uniqueness, composite organization scope, immutable exact plan/steps, future snapshot not active recipient, consumed-signal uniqueness, append-only timeline update/delete rejection, nullable legacy references/no backfill, atomic confirmation rollback and immutable policy execution after current-policy edits.
- [ ] Map exact plan/recipient evidence, run version/lease/pause/reason/time fields, consumed response IDs, events and unique activation/outbox identities. Update synthetic DEMO seed explicitly; never relabel legacy confirmation as approved.
- [ ] Generate migration with `dotnet ef migrations add Phase9Escalation --project src/backend/CriticalAlerts.Infrastructure --startup-project src/backend/CriticalAlerts.Api --output-dir Persistence/Migrations`; inspect SQL and apply to fresh empty PostgreSQL and retained pre-Phase-9 data in tests.
- [ ] GREEN full infrastructure and API integration projects, then full backend solution regression.
- [ ] Commit `feat: persist phase 9 escalation state`.

## Task 5 — Database-clock scheduler and guarded worker

Files: create `src/backend/CriticalAlerts.Infrastructure/Escalation/DatabaseClock.cs`, `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationScheduler.cs`, `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationRunRepository.cs`, `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationServiceCollectionExtensions.cs`, `src/backend/CriticalAlerts.Application/Escalation/EscalationWorkerOptions.cs`, `src/backend/CriticalAlerts.Application/Escalation/SimulationEscalationEnvironmentGuard.cs`, `src/backend/CriticalAlerts.Worker/SimulationEscalationWorker.cs`.

Modify `src/backend/CriticalAlerts.Infrastructure/Persistence/AlertMutationLock.cs`, `src/backend/CriticalAlerts.Worker/Program.cs`, `src/backend/CriticalAlerts.Worker/appsettings.json`, `src/backend/CriticalAlerts.Worker/appsettings.Development.json` only where required. Tests: create `tests/CriticalAlerts.Infrastructure.Tests/EscalationSchedulerTests.cs`, `tests/CriticalAlerts.Application.Tests/SimulationEscalationEnvironmentGuardTests.cs`; modify `tests/CriticalAlerts.Application.Tests/WorkerConfigurationTests.cs`.

- [ ] RED only Active eligible exact-version alerts scheduled, one run under concurrent schedulers, PostgreSQL due query, stored deadline across restart, two claimers, lease expiry recovery, stale owner refusal; Development/Test allowed, enabled Staging/Production fail startup.
- [ ] Implement bounded polling with stable worker owner and separate enabled/batch/poll/lease settings. Disabled by default outside explicit simulation configuration. Claim alert lock before run lock; no run-then-alert inversion. Add no recipient effects until next slices.
- [ ] GREEN application/infrastructure projects and backend regression. Record query plans/index rationale if new due indexes are needed.
- [ ] Commit `feat: schedule deterministic escalation runs`.

## Task 6 — Confirmed backup activation

Files: create `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationRunProcessor.cs`; modify `src/backend/CriticalAlerts.Domain/Alerts/AlertRecipientSelection.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AlertConfigurations.cs`, `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationRunRepository.cs`; test `tests/CriticalAlerts.Infrastructure.Tests/EscalationActivationTests.cs`.

- [ ] RED exact preconfirmed step only; no original primary redispatch; no version/content mutation; source/provenance visible; two processors produce one activation; rollback and expired-lease recovery; inactive/missing/foreign/channel-ineligible backup creates visible failure/manual fallback and zero partial selection/outbox; no replacement lookup.
- [ ] Activate exact snapshots under alert-then-run locks, recheck lifecycle/responsibility before any effect, persist unique step/recipient provenance and append-only timeline. Couple dispatch queue insertion with activation in Task 7; do not enable partial activation behavior in a runnable worker before atomic outbox integration is green.
- [ ] GREEN infrastructure and domain projects; regression solution.
- [ ] Commit `feat: activate confirmed escalation recipients`.

## Task 7 — Identifier-only outbox integration

Files: create `src/backend/CriticalAlerts.Domain/Escalation/EscalationDispatchRequested.cs`; modify `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationRunProcessor.cs`, `src/backend/CriticalAlerts.Infrastructure/Dispatch/OutboxDispatchProcessor.cs`, `src/backend/CriticalAlerts.Infrastructure/Dispatch/DispatchServiceCollectionExtensions.cs`; test `tests/CriticalAlerts.Infrastructure.Tests/EscalationDispatchTests.cs`.

- [ ] RED one step outbox and one logical recipient/channel attempt with two workers/replay/restart; strict payload allowlist and organization/version/run/step membership; malformed/extra IDs rejected; original manual selections untouched; crash rolls back activation, timeline, signal, outbox and step together; provider failure remains visible.
- [ ] Reuse existing dispatch adapters/retries; process only supplied newly activated selection IDs. Never call adapters from escalation. Atomic outbox insertion and stable logical keys complete activation before enabling its worker execution.
- [ ] GREEN full infrastructure project and backend regression; inspect captured payload/audit/log sentinels.
- [ ] Commit `feat: dispatch escalation through outbox`.

## Task 8 — Stop conditions, consumed signals and overrides

Files: modify `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationRunProcessor.cs`, `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientResponseService.cs`, `src/backend/CriticalAlerts.Infrastructure/Alerts/AlertLifecycleService.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/AlertMutationLock.cs`, `src/backend/CriticalAlerts.Api/Authentication/DevelopmentAuthenticationServiceCollectionExtensions.cs`, `src/backend/CriticalAlerts.Api/Program.cs`, `src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`.

Create `src/backend/CriticalAlerts.Application/Escalation/EscalationOverrideContracts.cs`, `src/backend/CriticalAlerts.Infrastructure/Escalation/EscalationOverrideService.cs`, `src/backend/CriticalAlerts.Api/Http/EscalationEndpoints.cs`; locate and reuse the existing lifecycle authorization constant instead of adding production permissions. Tests: create `tests/CriticalAlerts.Infrastructure.Tests/EscalationConcurrencyTests.cs`, `tests/CriticalAlerts.Api.IntegrationTests/EscalationOverrideTests.cs`; extend `tests/CriticalAlerts.Infrastructure.Tests/ResponseLifecycleConcurrencyTests.cs`.

- [ ] RED lifecycle/responsibility wins under shared alert lock, opposite lock ordering is correctly serialized, acknowledgement does not stop; decline/unavailable response consumed once across multiple steps, polls, restart and two workers; pause retains pending signals; rollback does not consume signals.
- [ ] RED authenticated roles/401/403/cross-org 404/exact-version 409, invalid state, mandatory key/reason, repeated pause/resume replay, same key/different operation or body conflict, audit/timeline cardinality and protected-value exclusion.
- [ ] Implement stop precedence, durable signal consumption and remaining-delay controls with database time and the common lock protocol. Resume of stopped/completed runs is rejected. Do not add permanent stop or responsibility transfer.
- [ ] GREEN infrastructure/API projects and backend regression. Generate concrete endpoint OpenAPI through existing tooling; complete contract verification in Task 9.
- [ ] Commit `feat: add escalation stop and override commands`.

## Task 9 — Live projection and API contract

Files: modify `src/backend/CriticalAlerts.Application/Responses/AlertLiveContracts.cs`, `src/backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs`, `src/backend/CriticalAlerts.Api/Http/ApiResponseMetadata.cs`, `src/backend/CriticalAlerts.Api/Program.cs`, `docs/api/openapi.json`; tests: modify `tests/CriticalAlerts.Api.IntegrationTests/AlertLiveAuthorizationTests.cs`, create `tests/CriticalAlerts.Api.IntegrationTests/EscalationContractTests.cs`.

- [ ] RED exact policy/version/eligibility/state/step/time/paused/stopped/exhausted timeline; recipient selection source and run/step provenance; legacy ineligible; failure/exhaustion fallback; no patientReference, approvedMessage, phone, providerReference or ciphertext; deterministic timeline ordering and existing role boundaries.
- [ ] Project only safe durable data and allowed actions. Generate OpenAPI using `scripts/verify-openapi.ps1` supported update workflow, inspecting its parameters first; do not hand-maintain mismatching JSON. Include concrete success/RFC 7807 and mandatory key metadata.
- [ ] GREEN full API integration project and `./scripts/verify-openapi.ps1`.
- [ ] Commit `feat: expose escalation live timeline`.

## Task 10 — Connected frontend review/live

Files: modify `src/web/lib/alerts.ts`, `src/web/features/connected/review-alert.tsx`, `src/web/features/connected/live-alert.tsx`, `src/web/tests/connected-review-directory.test.tsx`, `src/web/tests/connected-responses.test.tsx`; create `src/web/tests/connected-escalation.test.tsx`.

- [ ] RED exact future backup plan/policy/revision visible before confirmation and submitted unchanged; mismatch recovery; DEMO state/timeline/source; acknowledgement distinct from acceptance; pause/resume double click sends once; uncertain retries preserve key/body; failed read retains last durable state; polling cleanup; no browser storage or zero-countdown mutation.
- [ ] Extend connected components and typed client, preserving current layout/accessibility and five-second read-only polling. Controls use server-authorized actions; legacy alerts show ineligible status. No retired prototype state/store imports.
- [ ] GREEN `npm --prefix src/web test -- --run`, `npm --prefix src/web run typecheck`, `npm --prefix src/web run lint`, `npm --prefix src/web run build`, `./scripts/verify-web-storage-safety.ps1`.
- [ ] Commit `feat(web): connect escalation status and controls`.

## Task 11 — Connected restart/concurrency proof

Files: modify `tests/e2e/closed-loop-system.spec.ts`, `scripts/system-e2e.ps1`; create `tests/e2e/escalation-system.spec.ts`; modify `tests/CriticalAlerts.Infrastructure.Tests/EscalationConcurrencyTests.cs` and harness configuration only as required.

- [ ] RED D: exact human confirmation → initial dispatch → PostgreSQL timeout → preconfirmed backup → one escalation outbox → simulated delivery → visible timeline.
- [ ] RED E: acknowledgement only still escalates. F: acceptance before deadline then worker restart/past deadline yields stopped run and zero backup selection/outbox/delivery.
- [ ] RED G: test decline and unavailable immediate eligibility and later polls do not reuse the signal. H: pause/restart/past original deadline yields no activation; resume restores remaining delay and executes once.
- [ ] RED I: two independent worker processes on one DB produce one backup selection, one logical RecipientActivated event, one step outbox, one logical attempt. Assert other required timeline event types also exist once each.
- [ ] Add inactive-backup visible failure/no-replacement and changed-review/legacy scenarios across real backend. Manipulate test-only persisted timestamps/DEMO fixture durations safely rather than expose a production clock-control endpoint. Preserve A/B/C and teardown guarantees.
- [ ] GREEN `npm run web:e2e`, `npm run web:e2e:system`, infrastructure concurrency project. Record exact counts, process restarts, direct PostgreSQL assertions and zero leftover owned processes/containers.
- [ ] Commit `test: add phase 9 restart and concurrency scenarios`.

## Task 12 — Full verification and review package

Files: modify `AGENTS.md`, `README.md`, `docs/product/workflow.md`, `docs/product/definition-of-done.md`, `docs/architecture/escalation.md`, `docs/architecture/alert-state-machine.md`, `docs/architecture/data-model.md`, `docs/architecture/recipient-selection-and-review.md`, `docs/architecture/simulated-dispatch.md`, `docs/security/threat-model.md`, `docs/security/logging-policy.md`, this plan; create `docs/superpowers/phase9-verification.md`. Update `docs/product/phase-approval-evidence.md` only with actual dated authorization/evidence, never inferred acceptance.

- [ ] Run `./scripts/test-all.ps1`: pinned locked restore; format; Release build; all backend projects including PostgreSQL/Testcontainers; full web tests/typecheck/lint/production build; OpenAPI; dependency scans; sensitive-data/storage checks; standalone Playwright and connected system E2E; API/worker/web container builds and web proxy check.
- [ ] Explicitly verify `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo`, `dotnet format src/backend/CriticalAlerts.sln --verify-no-changes --no-restore --verbosity minimal`, `dotnet build src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo`, `dotnet test src/backend/CriticalAlerts.sln --configuration Release --no-build --nologo --logger trx` and retain exact counts per project. Avoid rerunning unchanged passing checks without reason; the recorded test-all stages can supply this evidence.
- [ ] Dependency evidence: `dotnet list src/backend/CriticalAlerts.sln package --vulnerable --include-transitive --no-restore`, `npm audit --prefix src/web --audit-level=high --omit=dev`; record any unavailable/failed check without claiming success.
- [ ] Fresh empty PostgreSQL migration and explicitly confirmed fictional demo reset through `scripts/db-migrate.ps1` / `scripts/db-reset-demo.ps1 -ConfirmDemoReset` against a verified isolated loopback simulation database, or the equivalent fully recorded system harness stages. Verify legacy data upgrade without policy fabrication and restart/concurrency independently.
- [ ] Inspect full diff, protected-value sentinel evidence, no real provider/AI/Phase 10, no historical migration edits and no weakened tests. Run `git diff --check`, `./scripts/verify-no-sensitive-data.ps1`, `./scripts/verify-web-storage-safety.ps1` and OpenAPI verification on final source.
- [ ] Record files, decisions, exact migration names, commands, exact test counts, security scope, limitations, all `REQUIRES_HOSPITAL_DECISION` items, proposed commit/tag and human review gate. Preserve earlier historical reports. Do not claim final Phase 9 acceptance or create a tag by inference.
- [ ] Commit `docs: complete phase 9 verification package`; stop for human review before Phase 10.
