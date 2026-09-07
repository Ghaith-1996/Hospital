# Phase 8.5 verification and review package

Date: 2026-09-05 (America/Toronto). Baseline: `c94401f`. Branch: `fix/phase-0-8-reintegration`.

CI follow-up: 2026-09-07. The first hosted run passed the checks preceding browser installation but hit GitHub's six-hour limit during browser extraction. [Playwright's upstream report](https://github.com/microsoft/playwright/issues/41000) identifies this hang on Node 24.16.0 and a fix in Playwright 1.60.0. Both test dependency pins/locks were updated from 1.55.1 to 1.60.0; Node and application dependencies remain unchanged. Browser installation is also bounded and installs the required headless shell. Earlier local browser evidence below used 1.55.1; the final hosted run verifies the corrected dependency from a fresh runner.

The next hosted run installed the browser and passed standalone smoke, then exposed a synchronization error in system Scenario A: its old draft-version assertion was already true before the approved-message save completed. The test now awaits the successful API response and its exact rendered version before navigating, preserving the application's unsaved-edit guard. Playwright 1.60.0 was also verified locally with a fresh headless-shell installation, 28 component tests, typecheck, lint and one standalone smoke test.

## 1. Executive verdict

The redesigned application now exercises the real Phase 0–8 simulation architecture: browser → Next.js → API → PostgreSQL → transactional outbox → worker → simulated adapter → durable delivery → practitioner response/responsibility → operator lifecycle. Local clean-checkout verification and hosted GitHub CI pass. The Phase 8.5 technical gate is complete; project-owner acceptance remains open.

## 2. Files changed

| Group | Main changes |
|---|---|
| Documentation | README, AGENTS, product workflow/decisions/definition of done, approval evidence register, Phase 5–7 architecture and containers, Phase 8.5 design/plan, backend/system reports and this package. Historical prototype evidence remains labelled historical. |
| Backend | New authorized Development/Test simulation location endpoint; Program OpenAPI normalization; concrete HTTP response, idempotency-header and multipart metadata; generated `docs/api/openapi.json`. |
| Frontend | Active route wrappers, session provider/switcher/shell, `features/connected/*`, development-auth and import clients, small alert DTO correction. Retired `features/alerts` prototype store/seed/selectors/types/workflow and unused prototype alert components. |
| Tests | Real-host contract/location tests; eight connected UI test files; real system A/B/C and standalone smoke. Retired local prototype unit/browser tests. |
| Scripts | Deterministic full semantic OpenAPI generation/verification, isolated system harness, active storage safety check, test-all sequencing and browser teardown. |
| CI | Retained all existing checks; added production web build, persistent-state guard and real system E2E. The harness rebuilds Next with its own API destination. |

Use `git diff --name-status c94401f..HEAD` for the exact versioned file inventory. Commits separate documentation, backend contract work, frontend slices, cutover, recovery fixes, and system/CI work.

## 3. Backend changes and preserved behavior

No domain, application, infrastructure, worker, migration, provider, or fixture implementation changed in this phase. Their diff from `c94401f` is empty. Exact versions, optimistic concurrency, approval invalidation, organization isolation, encrypted content/source history, inactive/revision rejection, idempotency, audit/outbox atomicity, bounded retries, and lifecycle/response races remain backend-owned and are covered by the complete retained suite.

The additional read-only `/api/v1/dev/location-context` returns only the authenticated organization's safe site/department identifiers and names under the existing draft-editor authorization policy. It uses the existing Development/Test authentication registration guard. IDs serialize as UUID strings. It grants no new workflow/lifecycle permission.

The generated contract previously omitted concrete success bodies and often declared a generic 200 for IResult endpoints. Endpoint metadata now declares actual DTO/status shapes (including 201 and 204), errors, required idempotency headers and multipart fields. Tests compare all runtime JSON semantically, ignoring object/set ordering and retaining meaningful array order. The frontend practitioner critical-field DTO was corrected from `normalizedValue` to the actual response field `value`.

## 4. Active screen/API mapping

All paths below have the `/api/v1` prefix.

| Screen | Actual API calls |
|---|---|
| Session switcher | GET `/dev/identities`, POST `/dev/session` with the listed handle only, GET `/me`. Principal changes remount protected content. |
| `/alerts/new` | GET `/dev/location-context`; POST `/alerts/drafts` with human-entered source, SBAR, location, urgency and critical fields. |
| `/alerts/[id]/compose` | GET/PATCH `/alerts/{id}`; PUT `/alerts/{id}/approved-message`; POST `/alerts/{id}/field-confirmations`; POST `/alerts/{id}/submit-for-confirmation`. |
| `/alerts/[id]/recipients` | GET draft and `/directory/practitioners`; PUT `/alerts/{id}/recipients` with exact version, manually chosen channels and displayed directory revisions. |
| `/alerts/[id]/review` | GET `/alerts/{id}/review`; POST `/alerts/{id}/confirm` with exact version and retained idempotency key. |
| `/alerts/[id]/live` | GET `/alerts/{id}/live` every five seconds/manual refresh; POST `/alerts/{id}/resolve` or `/cancel` with exact confirmed version/key. |
| `/my-alerts` | GET `/my-alerts`, scoped by backend practitioner mapping. |
| `/my-alerts/[id]` | GET `/my-alerts/{id}`; POST `/my-alerts/{id}/opened` and `/responses`, each with an idempotency key. |
| Directory/import | GET `/directory/practitioners`; POST multipart `/directory/imports/preview` and `/directory/imports` with the server preview token. |

`/alerts` opens a saved alert by ID because the retained API has no operator-wide list endpoint. Legacy detail/sent URLs lead to live status; the legacy respond route leads to the connected practitioner detail.

## 5. State storage and recovery proof

The active provider no longer mounts a prototype store. Its local alert/recipient/response engine and independent clinician seed were deleted. The static gate scans active app, connected/session features, layout and API clients for localStorage/sessionStorage and prototype-store dependencies; it passes. The browser has transient form and request state only. No case data persistence workaround was added.

Stale saves retain unsaved buffers and disable further writes until explicit discard/load. Directory-only conflicts also reload current evidence when the draft version itself is unchanged. New/compose/recipient/import edits have unload/link/identity-switch guards; Chromium history traversal uses Navigation API cancellation. Other browser engines were not verified. Saved state recovers through API reads after refresh; unsaved text intentionally does not survive refresh.

Uncertain confirmation/response attempts retain their exact in-memory key and payload. A successful mutation followed by a failed read retries the read and blocks another mutation; it does not generate a new command key. These keys are not persisted across browser restart; the durable backend still prevents invalid duplicate transitions.

## 6. Real E2E proof

- **A:** Backend operator sign-in; browser draft/source/SBAR/critical value creation; message approval; two manually selected active practitioners/channels; field reconfirmation; exact review; double-click-safe confirmation; visible DispatchQueued; worker delivery; server-linked Riley opens, acknowledges and accepts separately; operator live view, resolve and reload. State crosses the real database/outbox/worker boundaries.
- **B:** Concurrent API update produces N+1; stale browser save rejects without changing durable content or discarding the browser buffer; explicit discard/load recovers N+1 and requires review again. Navigation cancellation retains unsaved content.
- **C:** Concurrent real-backend confirmation with one key returns success plus replay success. Direct PostgreSQL assertions find exactly one matching outbox item, one logical recipient selection and one delivery attempt for the scenario.

Clean checkout `528c2d3` ran all three successfully (14.6 seconds) and standalone smoke (one test, 4.5 seconds). System teardown reported `container_remaining=0 live_owned_processes=0 ports_closed=1`. The harness uses direct DLL/Node server processes and fails on cleanup residue. See `system-phase85-report.md` for additional runs and fictional screenshot paths.

## 7. Exact local verification

A new local clone at `D:\hospital\phase85-clean-verification` started without build artifacts or node_modules. It was fast-forwarded through the reviewed implementation. Backend verification source is unchanged between `1bc49e7` and `528c2d3`; web application source is unchanged between `0e58ed1` and `528c2d3`. Later changes in that interval harden harness/CI only.

Pinned .NET SDK 10.0.100 and Node 24.16.0 were placed on PATH; the existing pinned Chromium cache was used. Initial sandbox network/Docker restrictions required approved escalation; no check was bypassed or weakened.

| Command/stage | Result |
|---|---|
| `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo` | Pass from clean checkout. |
| `dotnet format src/backend/CriticalAlerts.sln --verify-no-changes --no-restore --verbosity minimal` | Pass. |
| `dotnet build src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo` | Pass, 0 warnings, 0 errors. |
| `dotnet test src/backend/CriticalAlerts.sln --configuration Release --no-build --nologo --logger trx` | **297 passed, 0 failed, 0 skipped:** domain 60, application 39, infrastructure/PostgreSQL 67, API integration 122, architecture 9. |
| `dotnet list src/backend/CriticalAlerts.sln package --vulnerable --include-transitive --no-restore` | No vulnerable packages reported for any of the 11 projects. |
| Root `npm ci --no-audit --no-fund` and web `npm ci --prefix src/web --no-audit --no-fund` | Pass, clean dependency installation. |
| `npm --prefix src/web test -- --run` | **28 passed in 8 files**, 0 failed. |
| `npm --prefix src/web run typecheck` and `run lint` | Pass, zero lint warnings/errors. |
| `npm audit --prefix src/web --audit-level=high --omit=dev` | Zero vulnerabilities reported. |
| Production Next build through system harness | Pass, 15 routes. |
| `npm run web:e2e` | **1 passed**, standalone API-unavailable smoke. |
| `./scripts/system-e2e.ps1` | **3 passed**, real connected scenarios; empty PostgreSQL migration and explicit fictional reset; cleanup pass. |
| API, worker, web `docker build` from clean source | All pass; images `critical-alerts-{api,worker,web}:phase85`. Docker warned that automatic Git provenance metadata was unavailable; source checkout IDs are recorded here. |
| `./scripts/verify-web-container.ps1 -WebImage critical-alerts-web:phase85` | Pass, real web proxy reaches separate synthetic API fixture container. |
| `./scripts/verify-openapi.ps1` | Pass, complete runtime semantic match. Focused contract tests: 19 passed; location tests: 5 passed, included in the API total. |
| `./scripts/verify-no-sensitive-data.ps1`, `./scripts/verify-web-storage-safety.ps1`, `git diff --check` | Pass. |

Logs/TRX remain outside the source repository under `D:\hospital\phase85-*.log` and `D:\hospital\phase85-test-results`. Review exposed genuine red tests for contract completeness, stale-form preservation, failed-refresh idempotency and keyboard/import recovery before their corrections. No claim is made that passing component mocks substitute for PostgreSQL or system tests.

The final local clean-checkout system run at `b9045d7` passed all three scenarios in 15.2 seconds, including cancellation and acceptance of same-document browser Back navigation. Teardown reported zero remaining containers, zero live owned processes and closed ports. Subsequent source changes are confined to CI, Playwright test dependencies, test synchronization and verification documentation; hosted verification below identifies its precise tested commit.

The retained fixture has 14 CSV rows representing **12 practitioners, 2 sites, 3 departments, 2 inactive practitioners, 6 specialties, two Martin surnames, Primary and Backup on-call examples, 3 rows missing optional endpoints, and 1 intentionally stale practitioner**. Parser, directory and seed tests use this same fictional fixture; the divergent frontend directory is retired. Existing reset tests cover explicit confirmation, environment/local-database guards and invalid reset targets.

## 8. CI status

[Hosted run 34147632647](https://github.com/Ghaith-1996/Hospital/actions/runs/34147632647) completed successfully on 2026-09-07 for commit `61b9c952f02e5091ad28fb5dbf1c3404912984df`. The single `verify` job and all 30 reported steps succeeded; none failed or skipped. [Machine-readable evidence](phase85-ci-evidence.json) records the exact job and step results. The subsequent report commit changes documentation only and intentionally skips another CI run; its application, tests, dependencies and workflow are identical to this verified commit.

Fresh Ubuntu runner results: **297 backend tests** (60 domain, 39 application, 67 infrastructure, 122 API, 9 architecture), **28 frontend tests in 8 files**, **1 smoke test in 4.6 seconds**, and **3 system scenarios in 14.0 seconds**. Backend tests report zero failures/skips; all browser scenarios ran. System teardown reports `container_remaining=0 live_owned_processes=0 ports_closed=1`. Release builds report zero warnings/errors. Full OpenAPI runtime comparison, both dependency scans, all three container builds, container API proxy, storage and repository safety checks passed. GitHub's Node cache post-step emits an upstream `url.parse()` deprecation warning but completes successfully.

The workflow runs on main, pull requests and this specific review branch. The branch was pushed using the existing Git transport. Draft PR creation through the GitHub connector failed because its account was not a collaborator. Automatic approval review rejected credential-assisted PR creation; that action did not execute. CI ran on the review branch without requiring a PR or changing repository settings.

| Step number | Workflow step | Hosted result |
|---|---|---|
| 1 | Set up job | success |
| 2 | Check out repository | success |
| 3 | Set up .NET SDK | success |
| 4 | Set up Node.js | success |
| 5 | Restore backend dependencies | success |
| 6 | Verify backend formatting | success |
| 7 | Verify OpenAPI contract | success |
| 8 | Scan backend dependencies | success |
| 9 | Build backend | success |
| 10 | Run backend tests | success |
| 11 | Install root test dependencies | success |
| 12 | Install web dependencies | success |
| 13 | Scan web dependencies | success |
| 14 | Run web unit tests | success |
| 15 | Typecheck web | success |
| 16 | Lint web | success |
| 17 | Verify active workflow storage safety | success |
| 18 | Build production web | success |
| 19 | Install Playwright browser | success |
| 20 | Run web smoke test | success |
| 21 | Run connected system E2E | success |
| 22 | Build API container | success |
| 23 | Build worker container | success |
| 24 | Build web container | success |
| 25 | Verify web container API proxy | success |
| 26 | Run repository safety check | success |
| 50 | Post Set up Node.js | success |
| 51 | Post Set up .NET SDK | success |
| 52 | Post Check out repository | success |
| 53 | Complete job | success |


## 9. Known limitations

Simulation only. No Phase 9 automation, real communication provider, external callback, hospital connector, AI or production identity. No clinical/production deployment approval. Reports/Settings and an operator-wide alert list are outside the retained API scope. Browser verification targets Chromium; broader browser/accessibility acceptance remains a review activity. Polling is intentionally sufficient for this simulation.

## 10. Governance

See `../product/phase-approval-evidence.md`. Phase 5 has an explicit dated owner-approval record. Phases 1–4 and 7 have review/closure/authorization evidence of differing precision; tags exist only for 2–5. Phase 0 acceptance remains blank, Phase 6 explicitly lacks final approval/tag evidence, and Phase 8 correction/publication authorization is distinct from final acceptance. Phase 8.5 implementation was explicitly requested. No historical approval or hospital policy has been fabricated.

## 11. Security boundary

GitHub repository metadata was read on this date and reported `private:false`, `visibility:public`. No repository visibility/settings mutation was performed. Fixture/source diff confirms no real data was introduced. The tracked-file safety scan found no tracked .env, credential literal or non-555 phone pattern. That is scoped scan evidence, not a claim that arbitrary future inputs are safe.

Generic SMS/voice wake-up content, protected detailed content, Development/Test fail-closed guards and existing response/lifecycle authority remain unchanged and covered by backend tests. The live projection exposes no clinical body, contact endpoint or raw provider payload. All unresolved production workflow/privacy/identity/provider/escalation/fallback decisions remain `REQUIRES_HOSPITAL_DECISION`.

## 12. Proposed next action

Project-owner review of the integrated Phase 0–8 simulation baseline is the next action. The technical gate passes; no human acceptance is inferred. Do not begin Phase 9 or alter repository visibility/settings.
