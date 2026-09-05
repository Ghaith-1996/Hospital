# Phase 8.5 — Phase 0–8 Reintegration and Compliance Gate

Status: corrective implementation authorized by the project owner's Phase 8.5 request; verification and human acceptance remain separate gates.

## Authority and boundary

This is corrective Phase 8.5, not Phase 9. The repository remains public. No repository visibility or settings changes are authorized. The supplied master build plan is background specification; the current request supersedes its private-repository instruction and later-phase work. All data remains fictional. No real PHI, providers, AI, hospital connections, production identity, production deployment architecture, or production policy assumptions are introduced. Unresolved production decisions remain `REQUIRES_HOSPITAL_DECISION`.

Preserve the frontend visual redesign: layout, typography, spacing, cards, buttons, clinician selector treatment, headers and textual status badges. Replace its active browser-state workflow with backend-persisted workflow. Phase 0–8 backend safety invariants remain authoritative; do not rewrite the domain to accommodate the prototype.

## Runtime flow

Browser → Next.js UI → `/api/v1/*` → ASP.NET Core application → PostgreSQL → transactional outbox → `CriticalAlerts.Worker` → simulated notification adapters → PostgreSQL delivery state → practitioner API response → PostgreSQL responsibility state → operator live-status API → browser.

The browser is never the source of truth for alert workflow state. Unsaved forms live in memory with navigation warnings. Saved content, recipient snapshots, responses and responsibility belong to PostgreSQL. No workflow content is persisted in localStorage or sessionStorage. API failure never produces a local success transition.

## Connected screens

- Development switcher: use existing `/api/v1/dev/identities`, `/dev/session`, `/dev/session/clear` and `/me`. Select only server-listed fictional handles; reload protected views after identity changes. Display `DEVELOPMENT AUTHENTICATION` and `SIMULATION MODE`.
- New: fetch organization-scoped fictional site/department choices, create through `createAlertDraft`, navigate to compose after success. Add a minimal authorized Development/Test-only context query only if no existing query provides those identifiers.
- Compose: `getAlertDraft`, `updateAlertDraft`, `setApprovedMessage`, `confirmCriticalField`, `submitAlertDraft`. Keep source, SBAR and approved message distinct. Every command uses the exact current draft version. Show each original value, approved value, unit and confirmation state.
- Directory/import: use backend search and preview/apply contracts, preserve server preview token and invalidate preview when file changes. Show active/stale/source/on-call/channel and name-disambiguation evidence.
- Recipients: manually choose every practitioner and channel from safe directory data; use `replaceAlertRecipients`. Return to compose because recipient replacement increments the version and invalidates critical confirmations. Approved-message edits do the same.
- Review: render only `getAlertReview` output, require deliberate confirmation, preserve one idempotency key and exact request across network retry, synchronously lock duplicate clicks. `confirmAlertReview` success is `DispatchQueued`, never delivery.
- Practitioner inbox/detail: backend mapping controls access; use `getMyAlerts`, `getMyAlert`, `markMyAlertOpened`, `recordMyAlertResponse`. Opening, acknowledgement, call-unit request, terminal disposition and responsibility remain distinct.
- Live: poll `getAlertLive`, render safe per-recipient attempt/response data and durable failure. Use `resolveAlert`/`cancelAlert` with exact version and idempotency key only when server allows. Manual fallback stays `REQUIRES_HOSPITAL_DECISION` with no route.

## Errors and concurrent edits

401 requires a backend development session; 403 explains lack of authority; 404 is inaccessible/missing; 409 requires reload and deliberate re-review, never overwrite. Directory revision/inactive-recipient failures direct operators back to recipient selection. Network failure retains the same uncertain idempotent attempt for retry. No independent frontend domain state machine is introduced.

## Contract and proof

Generate OpenAPI 3.1 from the actual API host in synthetic Test configuration with an ephemeral data-protection key. Compare semantic JSON, ignoring object key order and explicitly unordered schema arrays while preserving meaningful array order. Compare all paths, operations, parameters, request/response metadata and components. CI fails on drift.

Real system Playwright scenarios run PostgreSQL 18, all migrations, explicitly confirmed fictional reset, API, worker, Next.js and Chromium. Scenario A traverses creation, SBAR, critical confirmation, two manual recipients, exact review, durable dispatch, physician open/acknowledge/accept, operator resolve and reload. B proves stale updates cannot overwrite. C proves real same-key replay produces one durable outbox request and one logical delivery set. Processes/containers are torn down on success and failure.

## Governance and acceptance

Inspect tags, history and committed review records before changing approvals. Evidence of implementation or a tag alone does not establish hospital approval. Record source/commit/date of project approval claims and explicitly identify absent evidence. Preserve all unresolved hospital decisions.

Completion requires exact counts and commands for clean backend/frontend/database/E2E/container/security/OpenAPI checks plus actual CI status. Unexecuted checks remain unverified. Stop after Phase 8.5 review package; do not begin Phase 9.
