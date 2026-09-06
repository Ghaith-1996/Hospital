# Phase 8.5 Reintegration Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for independent implementation and review tasks; execute connected frontend changes in this session with TDD.

**Goal:** Connect the redesigned frontend to the authoritative Phase 0–8 simulation backend and prove the durable closed loop.

**Architecture:** Keep existing application services, PostgreSQL transactions, exact-version controls and simulation worker. Replace browser domain state with typed API clients and small presentation components. Generate the committed API contract from the executable host.

**Tech Stack:** pinned .NET 10/C# 14, PostgreSQL 18, Next.js/React/TypeScript, Node 24, xUnit/Testcontainers, Vitest/Testing Library, Playwright, PowerShell, Docker.

**Spec:** `docs/superpowers/specs/2026-09-05-phase-0-8-reintegration-design.md`

## Global constraints

Fictional data only; public repository unchanged; no Phase 9, real integrations, AI, production identity or policy. Existing backend invariants remain authoritative. No secrets in Git. Dedicated branch `fix/phase-0-8-reintegration`; small focused commits; no destructive Git commands. Each behavioral change starts with a failing test, ends with targeted verification and diff inspection.

## Task 1: Design and approval evidence

Files: new Phase 8.5 spec/plan; `docs/adr/*`, `docs/product/product-decisions.md`, `docs/product/definition-of-done.md`, README.

- [x] Record current request, runtime architecture, safety boundaries and acceptance gates.
- [x] Inspect `git tag -n`, `git log`, historical approval/closure records; record precise evidence in a governance document.
- [x] Reconcile contradictory current claims without inventing approvals. Commit documentation separately.

## Task 2: Generated OpenAPI and simulation location context

Files: API HTTP endpoints/Program; focused API integration tests; `scripts/verify-openapi.ps1`; new export/semantic comparison helper; `docs/api/openapi.json`.

- [x] Write failing real-host tests for complete generated contract comparison and Development/Test organization-scoped location query (anonymous, wrong role, foreign scope, disabled environment).
- [x] Run red tests using pinned local .NET runtime; record exact outcome.
- [x] Add the smallest read-only simulation context endpoint if no existing endpoint exposes selectable site/department IDs. Return safe identifiers/names only.
- [x] Generate contract from actual host with ephemeral synthetic configuration; compare semantic JSON comprehensively, test changed routes/methods/schema/status and harmless ordering.
- [x] Run focused API and script tests; inspect diff and commit.

## Task 3: Authentication and connected operator forms

Files: `src/web/lib/development-auth.ts`, existing `lib/alerts.ts`; layout/switcher; new connected workflow components; new/compose routes; focused component tests.

- [x] Write failing UI tests for server session switching, draft creation/load, loading/errors, SBAR save, unresolved fields, deliberate confirmation and stale reload.
- [x] Connect existing seeded identity endpoints, clear protected rendered state on switch and derive displayed roles from `/me`.
- [x] Connect create and compose to exact API types; use server location choices. Keep original source distinct from SBAR and message.
- [x] Protect in-memory dirty edits on navigation/refresh and explicit discard; never use persistent browser storage.
- [x] Run Vitest targeted tests, typecheck/lint; inspect diff and commit focused slices.

## Task 4: Directory, recipients and exact review

Files: directory/import routes and API client; recipients/review routes; shared directory cards; UI tests.

- [x] Write red tests for freshness, same-name disambiguation, inactive disabled controls, explicit channels, preview invalidation, exact review and duplicate clicks.
- [x] Connect search and CSV preview/apply with server preview token. Render safe source, role, site, department, on-call and channels.
- [x] Save complete manual recipient set with current version and presented revision; return to compose for renewed confirmation.
- [x] Render exact server review; keep a single confirmation attempt key/version across uncertain retries and lock clicks before awaiting fetch.
- [x] Show `DispatchQueued` after success and navigate to live; run targeted tests and commit.

## Task 5: Durable status, practitioner response and lifecycle

Files: live, my-alerts/detail, alert index/details routes; connected response/status components; UI tests.

- [x] Write red UI tests for distinct delivery/open/acknowledge/accept, provider failure, unauthorized inbox, lifecycle and refresh/poll cleanup.
- [x] Use existing live/inbox/response/lifecycle client methods; no frontend delivery timer or local response engine.
- [x] Keep each idempotent action's uncertain attempt stable; display server results and safe fallback placeholder.
- [x] Run targeted UI tests/typecheck/lint and commit.

## Task 6: Real system harness and CI

Files: `tests/e2e/closed-loop-system.spec.ts`, harness/config under scripts, smoke tests, `.github/workflows/ci.yml`, `scripts/test-all.ps1`.

- [x] Write real-browser A/B/C scenarios from spec, including database counts for outbox/delivery uniqueness.
- [x] Start isolated PostgreSQL 18, migrate/reset explicitly, API/worker/web with synthetic ephemeral secrets, and Chromium; always teardown resources.
- [x] Run scenarios red, repair integration mismatches with regression tests, then run green. Do not substitute mocked routes for system proof.
- [x] Preserve existing CI steps; add contract generation, system E2E, production web build and storage safety enforcement.
- [x] Verify authoritative directory fixture coverage; remove unused prototype state/models/components/tests after active routes are connected. Search all obsolete store symbols and storage access.

## Task 7: Clean verification and review package

Files: active architecture/product/docs and Phase 8.5 verification report.

- [x] Update active workflow documentation without erasing historical evidence.
- [x] Run clean locked restore, format, Release build, all backend tests including PostgreSQL/API/architecture; empty migration and guarded reset.
- [x] Run clean npm installs, unit tests, typecheck, lint, production build, smoke and real system E2E.
- [x] Build API/worker/web containers and verify internal API proxy; run dependency and sensitive-data scans; generated OpenAPI comparison; diff/scope review.
- [ ] Repeat required verification from a clean checkout of committed work. Record exact commands, counts, skips/failures and CI job/step status.
- [ ] Obtain independent code/spec review, correct findings, then produce the user-requested twelve-part review package. Human acceptance remains external; no Phase 9.

## Execution notes

- Baseline: `c94401f`, clean working tree on arrival, branched from `fix/vercel-playwright-dependency` to the requested corrective branch.
- Ruling: current explicit Phase 8.5 implementation request authorizes this concrete integration design; skill approval prompts do not require redundant permission.
- Ruling: user supplied master plan at `C:/Users/ghait/Downloads/hospital-critical-alert-platform-master-build-plan.md`; it is background, not authorization for its embedded Phase 0 restart/private visibility/later-phase instructions.
- Tool preflight: PATH has .NET 9 and Node 22; repository/local folders contain alternate runtimes to inspect. Docker named-pipe access initially denied in sandbox; verify through authorized escalation before declaring unavailable.

- Verification: clean clone passed297 backend tests (60 domain,39 application,67 infrastructure,122 API,9 architecture),28 frontend tests,3 real system scenarios and1 smoke; generated contract, scans, format, builds and proxy passed. See phase85-verification.md for exact evidence and hosted CI status.
- Review corrections: full OpenAPI success/header/form metadata, explicit stale-buffer discard, stable uncertain actions/read recovery, same-version directory reload, keyboard menu focus, browser history cancellation, baked API URL and process teardown in CI. Independent broad review findings were corrected.
