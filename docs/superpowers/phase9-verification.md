# Phase 9 implementation and verification ledger

Plan: `plans/2026-09-22-phase-9-escalation.md`. Baseline `9e312b2`; branch `feature/alerts-and-escalations`. Implemented and technically verified on 2026-09-22. Ready for owner review; human acceptance remains pending. Phase 10 has not started.

## Authority and decisions

- User explicitly authorized the supplied Phase 9 specification and sequential implementation. Earlier Phase 8.5 scope statements are historical; no hospital policy or retrospective acceptance is inferred.
- Reuse existing policies, directory revision calculation, alert mutation lock, transactional outbox, channels and connected UI. Ponytail's reuse/minimal-change guidance is subordinate to all safety and test requirements.
- Task 2 includes its additive snapshot storage prerequisite; task 4 adds runs/events. A third additive migration closes published-policy step insertion. Approval is testable before enabling automation; historical migrations are unchanged.
- The external master build plan was not found in the repository or available attachment/document search. Existing translated Phase 0 architecture/ADRs and the complete supplied Phase 9 plan are the available specification; no missing production decisions are inferred.

## Environment and commands

- .NET SDK `C:\Users\ghait\.dotnet\dotnet.exe`: 10.0.100; Node 24.16.0.
- Started existing Docker Desktop; engine 29.5.2 responds.
- `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo`: pass, all 11 projects. Log: temporary `hospital-phase9-restore.log`.

## Task status

1. Complete in `df6c960`: specification/safety boundary. Documentation-only; `git diff --check` passed.
2. Complete in `5468d16`. Added optional wire fields that are mandatory for new successful confirmations, safe conflicts, database-clock review, locked evidence, immutable exact-version snapshot storage, additive migration `20260922144312_Phase9ConfirmedEscalationPlan`, and connected review display/request binding. This slice alone did not enable worker automation.
   - RED: two PostgreSQL tests failed because missing approval was accepted (200 rather than 409) and review lacked `escalationPlan`.
   - RED: snapshot SQL mutation succeeded before the immutable-history trigger; failed as expected once Docker recovered. One preceding test run was an environment failure (Docker unavailable), not a behavioral RED.
   - RED: frontend future-backup visibility test failed; four retained tests passed. Initial sandbox startup failure was rerun with authorized filesystem access.
   - GREEN: 128 API integration tests, 67 infrastructure tests, 60 domain tests, 29 frontend tests (8 files); no failures/skips in these suites. Typecheck and lint passed. Infrastructure regression took 17m17s including container/database work.
   - `dotnet format ... --no-restore`, OpenAPI regeneration, sensitive-data scan, active storage-safety scan and `git diff --check` passed. System replay scenario now sends the exact reviewed plan; system execution remains a later gate.
3. Domain run behavior: 10 new executable tests first failed against unimplemented methods, then the complete domain project passed 70/70. Exact alert version, precedence, UTC, pause/resume remaining delay, negative-response consumption, exhaustion and lease recovery are covered. Persistence mapping follows in task 4; this slice alone does not enable a worker.
4. Complete in `47cd83a`: durable run/event persistence.
5–7. Complete in `9ff96f6`: database scheduling, activation and outbox.
8–10. Complete in `b0aa4f0`: overrides, projection and UI; reviewed recovery fixes recorded below.
11–12. Complete: all system scenarios and required final verification gates passed, with documentation and safety-control mapping below. Stopped for owner review.

## Final gate

`scripts/test-all.ps1` completed successfully on 2026-09-22 with exit 0. The complete retained backend suite uses real PostgreSQL/Testcontainers for relational tests; no tests below were skipped.

| Gate | Result |
|---|---|
| Locked .NET restore; root/web `npm ci` | Passed |
| .NET formatting; Release build | Passed; build 0 warnings, 0 errors |
| Domain | 70 passed |
| Application | 39 passed |
| Architecture | 9 passed |
| API integration | 129 passed |
| Infrastructure/PostgreSQL integration | 89 passed |
| Complete backend total | **336 passed, 0 failed, 0 skipped** |
| Web unit/component tests | **31 passed**, 8 files |
| Web typecheck, lint, production build | Passed |
| OpenAPI | Complete runtime 3.1 contract matches committed document |
| NuGet dependency scan | No vulnerable packages reported in all 11 projects |
| Production npm dependency audit | 0 vulnerabilities |
| Sensitive-data and persistent web workflow storage scans | Passed, including rerun after final documentation edits |
| Playwright unavailable-API browser smoke | **1 passed** |
| Connected system A–I (G covers both negative dispositions) | **10 passed**, 6.9 minutes |
| Empty PostgreSQL 18 migration and fictional reset | Passed in isolated system harness |
| API, worker, web Docker builds | All passed |
| Built web container forwarding to separate API container | Passed |
| Production and dispatch-disabled escalation startup probes | Both rejected as required |

System evidence: deadline backup activation/delivery; acknowledgement does not stop; acceptance survives restart/deadline; decline and unavailable expedite; pause survives restart/deadline and resume activates once; two workers create one logical activation/outbox/delivery. Teardown reported `container_remaining=0 live_owned_processes=0 ports_closed=1`. Temporary full-gate log: `hospital-phase9-final-gates.log`; synthetic screenshot/log directory: `critical-alerts-system-be70a671ea944660a50b7970a8ce2657`, both under the host's temporary directory and not committed.

The exhausted-state screenshot was visually inspected. The paused-state capture originally showed transient loading; its test now explicitly awaits the restored Paused state after reload. The focused H rerun passed (1 test, 2.2 minutes) and its screenshot was visually inspected: Paused, persisted remaining delay, Resume control and ordered timeline were visible after restart/reload. Its teardown also reported zero containers/processes and closed ports. Evidence: temporary `hospital-phase9-pause-evidence.log` and `critical-alerts-system-d7f0b1d943c44dcf8e3df7e0838bf701/screenshots/08-escalation-paused-after-restart.png`. Only that verified test-evidence assertion and documentation changed after the successful full run; application code is unchanged.

Non-failing tooling output included npm's transitive `whatwg-encoding` deprecation notice and Playwright's color-environment warning. No required gate was omitted. Production decisions remain `REQUIRES_HOSPITAL_DECISION` as listed below; passing these gates does not constitute human acceptance.

### Task 4 — durable persistence
Added nullable exact-version binding (legacy rows remain disabled), durable leases/pause delay, scoped uniqueness and append-only safe events. PostgreSQL tests first failed because policy/event mutation was allowed, then passed with database triggers. Full infrastructure suite: 70 passed, no skips. No historical migration changed. New migration is additive; policy activation status alone remains editable.


### Tasks 5–7 — scheduler, activation and outbox
Implemented PostgreSQL clock scheduling and recoverable claims, alert-before-run locking, exact approved backup activation and atomic identifier-only outbox. Activation and scheduling were tested together because they share the durable transaction. Three scheduler tests: two behavioral RED then three GREEN. Eight scheduler/dispatch tests exercise concurrency, restart leases, legacy exclusion, accepted responsibility, acknowledgement and negative response behavior. A dispatch test exposed original-recipient leakage into escalation; original and escalation jobs now select disjoint approved selections. Full infrastructure regression: 78 passed, no skips. Worker escalation is explicitly disabled by default and fails closed outside Development/Test or without simulated dispatch. No new provider or dependency.


### Tasks 8–10 — controls, safe live projection and connected UI
Pause/Resume reuse lifecycle authorization, locking, idempotency and compact result references. Five PostgreSQL tests verify validation, replay, persisted delay, restart, read-only projection and isolation. API test verifies permitted/rejected actions, version conflicts and replay. Frontend test failed for missing pause control, then passed with same-key uncertain retry/double-submit protection. Full backend regression: Domain 70, Application 39, Architecture 9, API 129, Infrastructure 83 (330 total, no skips). Web: 30 passed; typecheck/lint passed. OpenAPI regenerated. Follow-up dispatch lock tightening will be covered with restart/concurrency verification.

### Follow-up policy, concurrency and review evidence

- A distinct immutable `DEMO-9` policy replaces the seed's legacy `DEMO-1` semantics for new reviews; no historical version is rewritten. Expected version test failed before the new seed. PostgreSQL initially allowed inserting a step after policy approval; the new focused test failed, then passed with `20260922165412_Phase9PublishedPolicySteps`. All 16 escalation integration tests passed at that checkpoint.
- Four additional PostgreSQL tests cover acceptance holding the alert lock while a due worker waits, plus suppression of already queued backup delivery after acceptance, resolution and cancellation. All 12 processor tests passed at that checkpoint. API checks additionally cover anonymous rejection, cross-organization 404, RFC7807 content type and protected-value non-disclosure; four focused API/review tests passed.
- Fresh read-only review through `superpowers:requesting-code-review` identified three Important findings. Each was reproduced: disappeared approved role caused an FK failure with no durable failure state; backup outage failed the whole previously delivered alert; changed live controls hid retry after a committed-but-lost Pause response.
- Fixes validate exact stored references under database locks and durably fail without substitution; retain the Active original recipient response path when backup delivery fails; and expose retained-command retry independently of current permissions. Review recheck found all three resolved and no remaining actionable issue within that recheck. Final infrastructure regression: 89 passed, no skips. Web regression: 31 passed in eight files.
- First system attempt could not launch the mismatched installed Playwright 1.55.1. Root `npm ci` restored pinned 1.60.0 and its Chromium was installed. A subsequent attempt incorrectly reused a production build containing the previous random API port; that run was discarded and its isolated services were verified removed. Final system verification always rebuilds for its own API address.
- Required dependency audit found the pre-existing Next.js 16.3.1 and transitive sharp vulnerabilities. Narrow lockfile update pins Next.js/eslint-config-next 16.3.6 and resolves sharp 0.35.4. References: [Next.js Windows advisory](https://github.com/vercel/next.js/security/advisories/GHSA-p293-qw3h-jr36), [Next.js AVIF advisory](https://github.com/vercel/next.js/security/advisories/GHSA-2xp9-vwfh-vxw4), [sharp advisory](https://github.com/lovell/sharp/security/advisories/GHSA-rgj7-g3m4-5g8c). Production npm audit now reports zero vulnerabilities; NuGet audit reports none across all 11 projects. No new direct package was introduced.

## Safety invariant evidence

Test filenames below are under `tests/`; the Phase 0 controls remain design requirements for integrations that are deliberately absent.

| Required invariant | Executable evidence or explicit design control |
|---|---|
| Fictional data only; no PHI, contacts or secrets in artifacts | `CriticalAlerts.Architecture.Tests/RepositorySafetyTests.cs`, sensitive-data scan, synthetic system fixtures and visual inspection; `docs/security/data-classification.md` and logging policy retain the Phase 0 boundary. |
| No autonomous AI clinical decisions, routing, dispatch or stop | No AI implementation introduced. Phase 0 `docs/security/threat-model.md` AI boundary; worker can consume only the exact human-approved stored recipient plan. `EscalationProcessorTests.cs` verifies exact activation and missing-reference failure. |
| Authorized human confirms exact content/version/values/units/recipients/channels/policy | API `AlertReviewTests.cs` and `AlertConfirmationTests.cs` reject missing/mismatched plan or changed directory evidence; exact snapshot, transactional rollback and replay tests. |
| Any editable content/recipient change invalidates approval | Domain `AlertStateMachineTests.cs`; API draft concurrency, review and confirmation suites. Escalation activates recipients already approved at the unchanged version. |
| Original source, structured content and approval remain separate | Domain `AlertSourceRevisionTests.cs`, infrastructure `AlertSourceRevisionPersistenceTests.cs` and `LegacySensitiveDataMigrationTests.cs`; data-classification design control for future transcription/suggestions. |
| Every critical number/unit requires human confirmation | Domain alert state machine and API review/confirmation tests retain unresolved-field rejection and exact version checks. |
| Generic SMS/voicemail; details only in authenticated UI | Infrastructure `SimulationChannelTests.cs` and application `DispatchContractTests.cs`; Phase 0 data-classification and production-readiness gates. DEMO escalation uses SecureMessage only. |
| Delivered, opened, acknowledged, responsibility accepted stay separate | Domain response/run tests, infrastructure processor/response tests and system E/F/G. Acknowledgement still escalates; accepted responsibility stops. |
| Unsupported channel states are `NotApplicable` | Existing dispatch contract/state, live projection and web response/live component regression tests; no change to channel capability semantics. |
| External callbacks authenticated, validated, replay-resistant and idempotent | No external endpoint added. Phase 0 threat-model provider boundary and production-readiness gates remain required. Existing simulation duplicate/out-of-order event tests remain green. |
| Delivery/provider failures visible | Infrastructure outbox/processor tests, including backup outage preserving the original recipient response path; safe live failure/fallback projection and frontend regression tests. |
| No secrets committed | Sensitive-data scan, repository safety tests, ignored local runtime secrets; generated system credentials remain ephemeral and outside tracked files. |

## Architectural decisions and limitations

- PostgreSQL remains the only saved workflow authority. The existing worker, outbox, simulation channels, server authorization, alert lock and live polling are reused; no extra service, queue, provider, frontend workflow store or direct dependency was added.
- The distinct `DEMO-9` seed has one 60-second SecureMessage step and one attempt. Its exact synthetic backup set is visible before confirmation. Empty steps exhaust visibly; runtime processing never invents a replacement. These are simulation fixtures, not hospital-approved policy.
- Database table locks stabilize approval evidence and reference validation. This intentionally simple design has a throughput ceiling; a future measured contention problem would justify narrower evidence locking.
- Only new exact approvals create runs. Legacy rows and alerts without snapshots remain escalation-disabled. Existing fictional databases need the explicit demo seed/reset path for the new policy; this does not authorize resetting real or unrelated data.
- Failures remain visible and manual fallback has no configured contact route. The software neither selects a production fallback nor assigns responsibility automatically.
- Browser state holds unsaved edits/presentation only. Polling never executes a deadline mutation; uncertain commands retain their original idempotency key and payload.
- No real provider, external callback, production identity, hospital integration, AI, real data or Phase 10 behavior is included. No repository settings/visibility, push, merge or tag was changed.

## Additive migrations

All are under `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/`, with generated designers and the current snapshot. Historical migrations are unchanged.

1. `20260922144312_Phase9ConfirmedEscalationPlan`: immutable organization/version-scoped approved plan.
2. `20260922162245_Phase9DurableEscalationRuns`: exact-version runs, durable scheduling/leases/pause state, scoped keys, append-only events and policy/step mutation controls; nullable legacy version, no backfill.
3. `20260922165412_Phase9PublishedPolicySteps`: prevent adding steps to an already approved policy version.

## Hospital decisions and human actions

Every production item below remains `REQUIRES_HOSPITAL_DECISION`:

- Clinical workflow, urgency authority, critical values/units and message content approval.
- Escalation timing, retry periods, policy ownership/version publication and eligible recipient/backup hierarchy.
- Directory identity mapping, on-call evidence, freshness, role/location eligibility and deactivation handling.
- Responsibility acceptance/transfer, acknowledgement and decline/unavailable semantics, lifecycle stop conditions and resolution/cancellation authority.
- Pause/resume permissions, reason codes, override limits and audit review responsibility.
- Manual fallback, provider outage, downtime, operational ownership and incident response.
- Privacy, consent, classification, retention/deletion, legal/regulatory conclusions and audit access.
- Hosting, deployment topology, security approval, key custody, production identity/roles and access lifecycle.
- Hospital/EHR/directory integration mappings, communications providers/channels, payload wording, callback authentication/replay policy and real contact routes.

The project owner must review the Phase 9 diff and evidence and record acceptance or corrections. Existing unrecorded earlier-phase acceptance remains a separate action. Any production enablement or Phase 10 requires separate authorization. Proposed final commit message: `feat: complete Phase 9 simulation-only escalation`. Proposed review tag: `phase-9`, only after owner acceptance; no tag is created here.

## Commands executed for the final gate

The entry point is `./scripts/test-all.ps1`, with the pinned .NET SDK on PATH and `PLAYWRIGHT_BROWSERS_PATH` set to the ignored repository browser cache. It executes:

```powershell
dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo
./scripts/verify-no-sensitive-data.ps1
./scripts/verify-web-storage-safety.ps1
./scripts/verify-openapi.ps1
dotnet format src/backend/CriticalAlerts.sln --verify-no-changes --no-restore --verbosity minimal
dotnet list src/backend/CriticalAlerts.sln package --vulnerable --include-transitive --no-restore
dotnet build src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo
dotnet test src/backend/CriticalAlerts.sln --configuration Release --no-build --nologo
npm ci --no-audit --no-fund
npm --prefix src/web ci --no-audit --no-fund
npm audit --prefix src/web --audit-level=high --omit=dev
npm --prefix src/web test -- --run
npm --prefix src/web run typecheck
npm --prefix src/web run lint
npm --prefix src/web run build
npm run web:e2e
./scripts/system-e2e.ps1
docker build --file src/backend/CriticalAlerts.Api/Dockerfile --tag critical-alerts-api:verification .
docker build --file src/backend/CriticalAlerts.Worker/Dockerfile --tag critical-alerts-worker:verification .
docker build --file src/web/Dockerfile --tag critical-alerts-web:verification .
./scripts/verify-web-container.ps1
git diff --check
```

The isolated system harness additionally executes the API's `database migrate` on empty PostgreSQL 18, then `database reset-demo --confirm-demo-reset`, starts the API/web/two workers, and runs `npx playwright test --config playwright.system.config.ts`. It verifies removal of its own services, container and ports. It does not reset the existing development container. Commands use `npm.cmd` on Windows.

Additional final startup probes launch the Release worker with escalation enabled in Production, then in Test with dispatch disabled. Both reject startup with the expected safe guard message before database registration. Earlier focused RED/GREEN tests, OpenAPI generation, `dotnet format --no-restore`, browser installation and lockfile security maintenance are described above. Full final results are recorded in the Final gate section.

The final focused screenshot rerun reused the same isolated harness, with a process-local PowerShell wrapper forwarding its `npx` invocation to `npx.cmd` with `--grep 'H: pause'`. No test was skipped or disabled in the full gate. Final `git diff --check` and `git diff --cached --check` passed.

## Files changed from `9e312b2`

The 62 repository-relative paths below include generated migration designers, model snapshot and OpenAPI. No unrelated user files were changed.

```text
AGENTS.md
README.md
docs/api/openapi.json
docs/architecture/data-model.md
docs/architecture/recipient-selection-and-review.md
docs/architecture/simulated-dispatch.md
docs/product/phase-approval-evidence.md
docs/product/workflow.md
docs/security/threat-model.md
docs/superpowers/phase9-verification.md
docs/superpowers/plans/2026-09-22-phase-9-escalation.md
docs/superpowers/specs/2026-09-22-phase-9-escalation-design.md
scripts/system-e2e.ps1
src/backend/CriticalAlerts.Api/Http/AlertLifecycleEndpoints.cs
src/backend/CriticalAlerts.Api/Http/AlertLiveEndpoints.cs
src/backend/CriticalAlerts.Application/Alerts/AlertLifecycleContracts.cs
src/backend/CriticalAlerts.Application/Alerts/AlertReviewContracts.cs
src/backend/CriticalAlerts.Application/Responses/AlertLiveContracts.cs
src/backend/CriticalAlerts.Domain/Alerts/Alert.cs
src/backend/CriticalAlerts.Domain/Delivery/ConfirmedEscalationPlan.cs
src/backend/CriticalAlerts.Domain/Delivery/EscalationEvent.cs
src/backend/CriticalAlerts.Domain/Delivery/EscalationRun.cs
src/backend/CriticalAlerts.Domain/Enums.cs
src/backend/CriticalAlerts.Domain/Policies/Policies.cs
src/backend/CriticalAlerts.Infrastructure/Alerts/AlertLifecycleService.cs
src/backend/CriticalAlerts.Infrastructure/Alerts/AlertReviewService.cs
src/backend/CriticalAlerts.Infrastructure/Alerts/EscalationPlanReview.cs
src/backend/CriticalAlerts.Infrastructure/Dispatch/EscalationProcessor.cs
src/backend/CriticalAlerts.Infrastructure/Dispatch/OutboxDispatchProcessor.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/ConfirmedEscalationPlanConfiguration.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/DeliveryConfigurations.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922144312_Phase9ConfirmedEscalationPlan.Designer.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922144312_Phase9ConfirmedEscalationPlan.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922162245_Phase9DurableEscalationRuns.Designer.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922162245_Phase9DurableEscalationRuns.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922165412_Phase9PublishedPolicySteps.Designer.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260922165412_Phase9PublishedPolicySteps.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/CriticalAlertsDbContextModelSnapshot.cs
src/backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs
src/backend/CriticalAlerts.Worker/Program.cs
src/backend/CriticalAlerts.Worker/SimulationDispatchWorker.cs
src/web/features/connected/live-alert.tsx
src/web/features/connected/review-alert.tsx
src/web/features/connected/use-idempotent-action.ts
src/web/lib/alerts.ts
src/web/next-env.d.ts
src/web/package-lock.json
src/web/package.json
src/web/tests/connected-responses.test.tsx
src/web/tests/connected-review-directory.test.tsx
tests/CriticalAlerts.Api.IntegrationTests/AlertConfirmationTests.cs
tests/CriticalAlerts.Api.IntegrationTests/AlertReviewTests.cs
tests/CriticalAlerts.Api.IntegrationTests/EscalationApiTests.cs
tests/CriticalAlerts.Domain.Tests/EscalationRunTests.cs
tests/CriticalAlerts.Infrastructure.Tests/EscalationOverrideTests.cs
tests/CriticalAlerts.Infrastructure.Tests/EscalationPersistenceTests.cs
tests/CriticalAlerts.Infrastructure.Tests/EscalationProcessorTests.cs
tests/CriticalAlerts.Infrastructure.Tests/OutboxDispatchProcessorTests.cs
tests/CriticalAlerts.Infrastructure.Tests/ResponseLifecycleConcurrencyTests.cs
tests/e2e/closed-loop-system.spec.ts
```

