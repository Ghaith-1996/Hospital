# Development History

Original plans, specs and verification reports were consolidated here on 2026-10-01; full originals are in git history at commit 2c18311 (docs/superpowers/, .superpowers/).

This is a historical record, not a governance document. Current rules are in [AGENTS.md](../../AGENTS.md). Normative behavior was promoted into the active docs (see [safety invariant traceability](../security/safety-invariant-traceability.md), [alert state machine](../architecture/alert-state-machine.md), [simulated dispatch](../architecture/simulated-dispatch.md), [observability](../architecture/observability.md), [containers](../architecture/containers.md)). Approval status per phase is in [phase approval evidence](../product/phase-approval-evidence.md); a passing test, a commit, a tag or an authorization to continue is not final acceptance, and no retrospective approval is created here. Dates are those recorded in the original documents; commit dates can differ because of rebases. Every production decision remains `REQUIRES_HOSPITAL_DECISION`.

Tags that exist: `phase-2` (67a52f5), `phase-3` (56a30eb), `phase-4` (494e973), `phase-5` (3d8bc56). No other phase tag was created.

## Phase 0 — Specification and repository rules

Scope: documentation-only baseline (README, AGENTS.md, product decisions, workflow, architecture, five ADRs, threat model, data classification, logging policy, production readiness gates, Phase 1 plan). No code.

Key decisions:
- Modular monolith, PostgreSQL, transactional outbox, human confirmation, no PHI in wake-up channels (ADRs 0001 to 0005). ADRs 0001, 0002 and 0005 stayed "proposed simulation baseline"; 0003 and 0004 are mandatory safety decisions.
- Delivered, opened, acknowledged and responsibility accepted are separate states; unsupported states are `NotApplicable`.
- Simulation-only values must be labelled; fictional `555` phone values only.

Completed: initial commit e862db0 (2026-08-19), together with the Phase 1 scaffold.

Verification: documentation checklist items all checked.

Open items: final Phase 0 package approval was never recorded (the checklist item stays unchecked and the product-decisions approval row is blank); this is a project-owner action.

## Phase 1 — Scaffold and local platform

Scope: empty modular-monolith projects, pinned toolchain, simulation-mode web shell, health endpoints, PostgreSQL 18 container and Testcontainers, CI baseline and repository safety scans. No domain behavior.

Key decisions:
- Pinned .NET SDK 10.0.100, Node 24.16.0, PostgreSQL 18; liveness separate from database readiness.
- Web shell shows simulation mode with no alert or dispatch controls.

Completed: 2026-08-19, in the initial commit (e862db0). Owner review recorded as approve-or-correct; no `phase-1` tag.

Verification: Testcontainers PostgreSQL 18 tests and the Playwright smoke test passed after Docker Desktop and the pinned browser were available; no counts are recorded in the surviving documents.

Open items: none beyond Phase 0 approval.

## Phase 2 — Database and domain foundation

Scope: domain entities, alert state machine, EF Core mappings, first PostgreSQL migration, demo seed and guarded demo reset.

Key decisions:
- `user_roles` unique on organization, user and role; one canonical critical-field confirmation per alert, draft version and field.
- Acknowledgement and responsibility acceptance stay separate records.
- Demo reset refuses Staging and Production.

Completed: 67a52f5 and tag `phase-2` (2026-08-19). The owner requested the uniqueness corrections before Phase 3.

Verification: organization isolation, optimistic concurrency, outbox atomicity and idempotency uniqueness tested against real PostgreSQL; counts not recorded.

Open items: the tag is implementation evidence, not hospital approval.

## Phase 3 — Development authentication and authorization

Scope: Development/Test-only fictional identities, role policies, organization scope checks and the user switcher.

Key decisions:
- Staging and Production refuse enabled development authentication at startup.
- Client-supplied user IDs, organization IDs and roles cannot select an identity.
- Operator, Administrator and Practitioner are authorized separately.

Completed: c77be06 and 56a30eb (closure tests), tag `phase-3` (2026-08-19).

Verification: authorization and fail-closed gate tests; counts not recorded.

Open items: production identity, SSO and role mapping are `REQUIRES_HOSPITAL_DECISION`.

## Phase 4 — Fictional directory and CSV import

Scope: directory integration boundary with a fictional CSV adapter, search, on-call and freshness display, administrator import.

Key decisions:
- CSV is an adapter over a normalized model; matching is by (organization, source system, source record) then (organization, simulation code), never by display name.
- Strict CSV contract (duplicate headers, malformed quotes, row width, non-UTC timestamps, non-synthetic endpoints rejected without echoing protected values).
- Preview does not mutate; apply is atomic; Operator and Administrator search, Administrator only imports. Contract details are in [directory integration](../architecture/directory-integration.md).

Completed: closure 494e973 and tag `phase-4` (2026-08-25); hardening 8fcb2f2, 98806af.

Verification: 2026-08-25, `scripts/test-all.ps1` passed with 118 backend tests, 9 web tests, typecheck, lint, 1 Playwright smoke test; the same passed from a clean clone.

Open items: production freshness window, deactivation, merge and source-of-truth rules, and stale-selection override are `REQUIRES_HOSPITAL_DECISION`. No separate final approval transcript is recorded.

## Phase 5 — Simulation-only alert drafting

Scope: typed draft creation and editing, protected source and SBAR storage, critical-field confirmation, submission to `PendingConfirmation`.

Key decisions:
- Every edit supplies the expected version, increments it and recreates critical fields as unresolved; earlier confirmations are historical only.
- Original source, structured SBAR and (later) approved message stay separate and protected.
- Stops at `PendingConfirmation`; no recipient selection or dispatch.

Completed: 39590e1, hardening 3d8bc56 (2026-08-27); owner approved Phase 5 on 2026-08-27, fast-forwarded to `main` and tagged `phase-5`.

Verification: `scripts/test-all.ps1` on 2026-08-27 passed with 147 backend tests, 16 web tests, typecheck, lint, 1 smoke test; the focused Phase 5 API suite passed 13 tests including log non-disclosure sentinels.

Open items: approval covers simulation Phase 5 only.

## Phase 6 — Recipient selection and exact review

Scope: manual recipient selection, separately protected approved message, exact review, idempotent human confirmation.

Key decisions:
- Selection is manual only (no AI, ranking or on-call defaults); each selection snapshots draft version, practitioner, role, channel, selector, directory revision and displayed on-call data.
- A full-set recipient replacement or message edit increments the draft version once and invalidates confirmations.
- Confirmation commits state transition, sanitized audit, idempotency record and one identifier-only outbox row atomically. See [recipient selection and review](../architecture/recipient-selection-and-review.md).

Completed: 473b0c0 (2026-08-29), merged as PR #1 (b02a387). No tag; the checklist records it accepted as the prerequisite to Phase 7, not final acceptance.

Verification: 2026-08-28, Release build clean; 160 backend tests (42 domain, 25 application, 64 API, 29 infrastructure), 17 web tests, typecheck, lint, build, 1 smoke test. Three architecture tests and the format check could not run from a Windows linked worktree mounted in Linux; fixed later in 7b62de4.

Open items: production eligibility, freshness, channels, confirmer roles, confirmation authority and wording are `REQUIRES_HOSPITAL_DECISION`; selecting an active-but-stale practitioner is a simulation assumption.

## Phase 7 — Simulated dispatch worker

Scope: Development/Test-only worker leasing outbox rows and delivering through typed simulated SecureMessage, SMS and Voice ports.

Key decisions:
- Fail closed unless `SimulationDispatch:Enabled` in Development or Test; `FOR UPDATE SKIP LOCKED` claims with owner and UTC lease expiry; bounded retry; duplicate and out-of-order provider events cannot regress state.
- Status projection exposes operational fields only. See [simulated dispatch](../architecture/simulated-dispatch.md).

Completed: 45f4024 (2026-08-29). Owner reviewed and separately authorized push; no `phase-7` tag.

Verification: 2026-08-29, 204 backend tests (9 architecture, 48 domain, 34 application, 42 infrastructure, 71 API), web 17 tests, 1 smoke test; fresh clone migrated through `20260829234957_Phase7SimulatedDispatch` and seeded 3 users and 12 practitioners.

Open items: providers, callbacks, retry service levels and operational ownership are `REQUIRES_HOSPITAL_DECISION`.

## Frontend prototype redesign

Scope: owner-authorized frontend-only phase (2026-08-30) reproducing a nine-state, eight-route operator and doctor workflow from the supplied mockup (`docs/design/frontend-prototype-nine-screen-mockup.png`).

Key decisions:
- Local reducer store with versioned `localStorage`, fictional users and a fixed "DEMO" escalation view; persistent `SIMULATION` treatment; Directory, Reports and Settings shown as "Coming later".
- Doctor response and escalation rendering marked `SIMULATION_ONLY_ASSUMPTION`; local rules (drafts operator-only until confirmed, no responses on resolved or cancelled alerts) were prototype-only.
- Superseded: Phase 8.5 retired the local store and reconnected the screens to the backend while keeping the visual language.

Completed: commits 5717131 to f67f18e (2026-08-30 to 2026-09-03), merged as PR #2 (5416a66, 2026-09-05); PR #3 fixed standalone builds.

Verification: Task 11 (2026-09-03) ran 71 web tests in 9 files, typecheck, lint, build, 4 Playwright tests; `scripts/test-all.ps1` was not run because the host had only .NET 9. These counts are historical and superseded.

Open items: none active; production response, escalation, amendment, reopening and lifecycle policy stayed `REQUIRES_HOSPITAL_DECISION`.

## Phase 8 — Practitioner response and closed loop

Scope: fictional practitioner inbox and responses, responsibility assignment, safe operator live projection, operator resolve and cancel; then compliance corrections from an external audit.

Key decisions:
- `PractitionerUserLink` is the sole user-to-practitioner authority; names and handles never map identity.
- Response and lifecycle semantics are in the [alert state machine](../architecture/alert-state-machine.md).
- Compliance corrections: protected patient reference, immutable source revisions, selection provenance, API v1 routing, OpenAPI 3.1, rate and body limits, guarded demo reset, expanded simulation roles, CI and container gates.

Completed: 85cdb4c and 43cae15 (2026-09-02); corrections 46d4ff2 (2026-09-05). The owner authorized correcting findings and publishing after verification; final integrated acceptance is not established by that authorization.

Verification: 2026-08-30, 237 backend tests, 22 web tests, 2 browser flows. 2026-09-05 after corrections: 273 backend tests (60 domain, 39 application, 9 architecture, 67 infrastructure, 98 API), 24 web tests, both browser flows, container builds, dependency scans.

Open items: response, transfer, release, escalation and fallback policy are `REQUIRES_HOSPITAL_DECISION`; GitHub rulesets and branch protection are an unchecked human administration action.

## Phase 8.5 — Reintegration

Scope: corrective phase reconnecting the redesigned frontend to the Phase 0 to 8 backend and proving the loop end to end.

Key decisions:
- Browser never the source of truth; unsaved forms in memory with navigation warnings; no workflow content in `localStorage` or `sessionStorage`; API failure never yields local success (see [containers](../architecture/containers.md)).
- OpenAPI 3.1 generated from the real API host and compared semantically in CI; new read-only `/api/v1/dev/location-context`.
- Real system harness (`scripts/system-e2e.ps1`) runs PostgreSQL 18, migrations, API, worker, Next.js and Chromium, then tears everything down.
- Public repository requirement supersedes the master plan's private-repository suggestion.

Completed: branch `fix/phase-0-8-reintegration`, merged as PR #4 (9e312b2, 2026-09-07). Hosted CI run 34147632647 at 61b9c95 passed all 30 steps.

Verification: 297 backend tests (60 domain, 39 application, 67 infrastructure, 122 API, 9 architecture), 28 web tests in 8 files, 1 smoke test, 3 system scenarios (A closed loop, B stale edit, C same-key replay), format, build, OpenAPI, scans, 3 container builds and proxy check. Playwright was moved from 1.55.1 to 1.60.0 to avoid a browser-extraction hang on Node 24.16.0.

Open items: project-owner acceptance of the integrated Phase 0 to 8 baseline is unchecked. Production location context, authentication and directory mapping are `REQUIRES_HOSPITAL_DECISION`.

## Phase 9 — Simulation-only escalation

Scope: deterministic DEMO escalation: exact approved plan, durable runs, worker activation through the outbox, pause and resume.

Key decisions:
- Seeded `DEMO-9` policy: one 60-second SecureMessage step; plan, policy identity and future backup recipients are shown at review and bound to the exact version. Evaluation rules are in the [alert state machine](../architecture/alert-state-machine.md) and [simulated dispatch](../architecture/simulated-dispatch.md).
- Additive migrations `Phase9ConfirmedEscalationPlan`, `Phase9DurableEscalationRuns`, `Phase9PublishedPolicySteps`; historical migrations unchanged.
- Table locks stabilize approval evidence; throughput ceiling accepted for simulation.
- Review fixes: missing approved role fails durably without substitution; backup outage keeps the original recipient response path; Pause retry stays available after a lost response.

Completed: PR #5 (a0eff26, 2026-09-22), final feature commit 1292526; verification package 44f1853. PR #6 (branch `feat/phase-9-escalation`) was closed without merge.

Verification: 2026-09-22 `scripts/test-all.ps1` exit 0: 336 backend tests (domain 70, application 39, architecture 9, API 129, infrastructure 89), 31 web tests, 1 smoke test, 10 connected system tests (D to I, G covers both negative responses), 3 container builds; teardown clean. Next.js and sharp were bumped to clear advisories.

Open items: owner acceptance pending (the owner later authorized Phase 10 without inferring it). Production items, all `REQUIRES_HOSPITAL_DECISION`: clinical workflow and message approval; escalation timing, retries, policy ownership and backup hierarchy; directory identity, on-call evidence, freshness and eligibility; responsibility, acknowledgement, decline and stop semantics and resolve or cancel authority; pause and resume permissions, reason codes and override limits; manual fallback, outage and downtime ownership; privacy, retention and legal conclusions; hosting, identity, key custody; hospital and provider integrations and callback policy.

## Phase 10 — Audit, observability and runbooks

Scope: audit API and viewer, append-only storage, local structured logs and metrics, truthful health, operational warnings, four runbooks, real PostgreSQL restore exercise.

Key decisions:
- `AuditReader` is Auditor or SystemAdministrator only; metadata is projected through allowlists; successful reads are audited and a failed audit append fails the request. Details are in [observability](../architecture/observability.md).
- PostgreSQL trigger rejects UPDATE, DELETE and TRUNCATE on `audit_events` (migration `20260919150045_Phase10AuditProtection`); no runtime bypass or retention deletion.
- Only local ILogger, `System.Diagnostics.Metrics` and health checks; no exporter. Worker delay never fails API readiness.
- Warning delay grace of 30 seconds is a simulation tolerance, not an SLA.

Completed: implementation 162a10b, documentation closure 8117d26 (2026-09-19), PR #7 (e79b598, merged 2026-09-25) with review-finding fix b86dbff. The owner explicitly accepted closure 8117d26 on 2026-09-19; acceptance recorded in da7f444. Publication, merge and tag were separate; no `phase-10` tag.

Verification: `scripts/test-all.ps1` exit 0 on 162a10b: 589 backend tests (domain 92, application 69, architecture 9, infrastructure 207, API 212), 58 web tests in 10 files, standalone browser test 1 passed, 13 connected scenarios, real restore plus injected-failure cleanup (AfterBackup, AfterRestore), OpenAPI match, scans, 3 container builds with web-proxy check, fresh migration and readiness. Restore durations (backup 295 ms, restore 587 ms, validation 2243 ms) are simulation measurements, not RPO or RTO.

Open items, all `REQUIRES_HOSPITAL_DECISION`: audit retention; export and legal hold; production audit reviewers and role mapping; central log destination and retention; SIEM; metric exporter; alert thresholds; incident severity; support ownership; provider and directory outage fallback; RPO, RTO, backup retention and disaster recovery authority. Known limitations are in [production readiness gates](../security/production-readiness-gates.md).

## Phase 11 — Speech and AI suggestions (in progress)

Scope: simulation-only speech transcription and structured suggestions; typing stays primary; features default disabled; provider output is immutable protected evidence applied only by explicit human Apply.

Key decisions and status are still in the live documents: [design](../superpowers/specs/2026-09-19-phase-11-speech-ai-suggestions-design.md), [plan](../superpowers/plans/2026-09-19-phase-11-speech-ai-suggestions.md), [architecture](../architecture/speech-and-ai-suggestions.md) and [verification record](../superpowers/phase11-verification.md).

Completed: PR #8 merged (846e021). Pre-rebase gate on 9e3af0a (that commit no longer exists in this repository's history): 673 backend tests, 69 web tests, 14 plus 2 connected scenarios; recorded as not verifying the rebased tree.

Open items: post-rebase verification and owner acceptance of Phase 11 remain pending. Real speech and LLM provider, residency, retention and raw-audio permission are `REQUIRES_HOSPITAL_DECISION`. No Phase 12 work belongs to this baseline (PR #9, a separate Phase 12 slice, was open at consolidation).

## Post-Phase 11 cleanup and documentation consolidation

Scope: remove redundant tests and dead code, then consolidate historical documentation.

Key decisions:
- 9209b41 (2026-09-29, "cleanup"): removed duplicate and low-value tests (40 files, net about 1,400 lines) and added a testing policy to AGENTS.md (E2E first; no unit tests written after code). Safety-invariant coverage was re-verified in [safety invariant traceability](../security/safety-invariant-traceability.md); `DispatchContractTests` no longer exists.
- 561b6b6 (2026-09-30): removed unused code, unused UI components, a large part of `globals.css` and duplicate authorization tests.
- PR #10 merge 2c18311 (2026-10-01).
- Branch `Refactor/Clean-up`: this consolidation moved plans, specs and verification reports for Phases 0 to 10 into this file, promoted still-normative rules into the active docs, and repointed links.

Verification: no behavior change in the documentation consolidation; link and marker checks were run by the consolidation writer.

Open items: Phase 9 and Phase 11 acceptance, Phase 0 approval, repository ruleset administration and all production decisions remain open as listed above.
