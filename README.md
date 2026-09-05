# Critical Clinician Alert Platform

Status: Phase 8.5 reconnects the redesigned frontend to the Phase 0–8 simulation backend. PostgreSQL owns saved drafts, recipients, dispatch, responses, and responsibility. Technical verification and human acceptance are separate gates. This repository remains public; this phase does not change GitHub settings.

This workspace defines a human-confirmed, closed-loop clinician alert simulation. It is not a hospital system, not a replacement for an EHR, pager, switchboard, scheduling system, or downtime process, and it is not approved for clinical use.

Phase 4 provides a fictional practitioner directory, CSV import adapter, validation/preview, and searchable directory UI. Phase 5 adds protected typed simulation alert drafting and SBAR confirmation. Phase 6 adds manual fictional-recipient selection, protected approved-message content, exact review, and idempotent human confirmation that creates an identifier-only outbox item. Phase 7 adds a Development/Test-only simulation worker, typed local channel adapters, deterministic provider-event scenarios, bounded retry, lease recovery, and safe delivery-status projection. Phase 8 adds simulation practitioner responses, call-unit requests, operator resolve/cancel actions, a safe manual-fallback placeholder, and a read-only operator status surface. Real providers, hospital connectors, SCIM, Graph, FHIR, AI features, Entra SSO, production identity, external callbacks, escalation automation, and Phase 9 remain out of scope.

## Connected simulation frontend

The server-controlled development identity switcher selects a seeded fictional identity. Create a draft at `/alerts/new`, save and confirm critical fields in compose, manually select directory recipients and channels, then review and confirm the exact server version. `/alerts/[id]/live` polls durable delivery and response state. `/my-alerts` uses the authenticated practitioner's backend mapping. Directory search and CSV preview/apply are connected; Reports and Settings remain unavailable. `/alerts` opens an existing alert by its server ID because the retained API has no operator-wide list endpoint.

The [Phase 8.5 design](docs/superpowers/specs/2026-09-05-phase-0-8-reintegration-design.md) supersedes the earlier prototype's local-state architecture while retaining its visual language. Unsaved edits remain in memory with navigation warnings. Refresh recovers only backend-saved content; no case details are written to persistent browser storage. The API and PostgreSQL must be available for workflow actions, and the worker must run to process the outbox.

## Source and decision precedence

The user-requested mandatory rules are binding. The attached master build plan is the project baseline and work plan; its recommendations are not hospital policy and do not authorize production behavior. These Phase 0 documents translate both sources into a simulation-safe specification.

When a real workflow, escalation, privacy, security, identity, directory, communications, retention, hosting, or integration decision is missing, the documents use the exact marker `REQUIRES_HOSPITAL_DECISION`. That marker must not be replaced by an invented production default.

The workspace is a Git repository. Product naming, ownership, and human role assignment remain project-owner decisions.

## Non-negotiable safety rules

- Use fictional hospital, employee, practitioner, patient, phone, and clinical data only.
- AI may assist with transcription and formatting, but may not diagnose, assign urgency, select final doctors, stop escalation, or dispatch autonomously.
- An alert may dispatch only after an authenticated human explicitly confirms the exact alert version and every recipient and channel.
- The original typed or transcribed content must remain separate from structured suggestions and approved content.
- Every critical number and unit requires explicit human confirmation.
- SMS and voicemail contain generic wake-up content only by default.
- Delivered, opened, acknowledged, and responsibility accepted are separate states.
- Never commit secrets or sensitive information.
- Use the documented modular-monolith stack, TDD, and real PostgreSQL integration tests when implementation begins.

The complete operating rules are in [AGENTS.md](AGENTS.md).

## Phase 0 review package

- Product and workflow: [product decisions](docs/product/product-decisions.md), [workflow](docs/product/workflow.md), [demo-data rules](docs/product/demo-data-rules.md), [terminology](docs/product/terminology.md), [definition of done](docs/product/definition-of-done.md).
- Architecture: [system context](docs/architecture/system-context.md), [containers](docs/architecture/containers.md), [data model](docs/architecture/data-model.md), [alert state machine](docs/architecture/alert-state-machine.md), [alert drafting](docs/architecture/alert-drafting.md), [directory integration](docs/architecture/directory-integration.md).
- Decisions: [ADRs](docs/adr/).
- Security and governance: [threat model](docs/security/threat-model.md), [data classification](docs/security/data-classification.md), [logging policy](docs/security/logging-policy.md), [production readiness gates](docs/security/production-readiness-gates.md).
- Next implementation package: [Phase 1 implementation plan](docs/superpowers/plans/2026-08-19-phase-1-scaffold-and-local-platform.md).

## Current status and review gate

The [approval evidence register](docs/product/phase-approval-evidence.md) distinguishes implementation, review/authorization records, tags, and final acceptance. Tags exist for Phases 2–5; they do not establish blanket Phase 0–8 approval. Phase 0 approval remains unrecorded, Phase 6 has no final acceptance evidence, and Phase 8 records correction/publication authorization separately from final acceptance. Hospital directory connections, production recipient eligibility, response/transfer semantics, channel mappings, provider contracts, callback authentication, production role mapping, lifecycle authority, and manual fallback routing remain `REQUIRES_HOSPITAL_DECISION`.

Phase 4 added a fictional CSV adapter over the shared practitioner directory model, strict validation/preview, duplicate detection, source-owned reconciliation, freshness filtering, and a searchable directory UI. Phase 5 adds protected typed source/SBAR drafts, optimistic draft versions, critical-field confirmation, and a compose UI. Phase 6 preserves the original source separately from SBAR and approved content, replaces recipients as one exact versioned set, shows safe directory evidence, and confirms only the exact reviewed version. Phase 7 consumes only that durable identifier-only outbox row: the worker leases it, reloads organization-scoped durable data, invokes typed deterministic simulation adapters, records safe delivery attempts/events, and completes, retries, or visibly fails the work. Simulation dispatch is fail-closed outside Development/Test and has no network/provider side effects.

Phase 8 explicitly links the fictional Riley user to one organization-scoped practitioner. Practitioner-only `/api/v1/my-alerts` routes expose the approved message for addressed Active alerts and record SecureMessage opening, acknowledgement, call-unit request, or one terminal accepted/declined/unavailable disposition with idempotent transactions. Acceptance creates a separate exact-version responsibility assignment. Operator/Administrator/ClinicalSupervisor-only `/api/v1/alerts/{id}/live` returns a safe refreshed projection without clinical content and exposes simulation-only resolve/cancel controls when their server-side preconditions hold. Delivery failures show a manual-fallback placeholder marked `REQUIRES_HOSPITAL_DECISION`; no production fallback route is configured. Both surfaces derive identity and organization from the server principal, fail closed outside Development/Test where applicable, and expose no encrypted storage values, contact endpoints, or raw provider data.

The Phase 6 specification is [recipient selection and review](docs/architecture/recipient-selection-and-review.md), the Phase 7 boundary is [simulated dispatch](docs/architecture/simulated-dispatch.md), and the Phase 8 design plus compliance corrections is [doctor response and closed loop](docs/superpowers/specs/2026-08-30-phase-8-doctor-response-and-closed-loop-design.md).

### Local verification

Create an ignored `.env` from `.env.example` with fictional local-only values, then run:

```powershell
Copy-Item .env.example .env
./scripts/dev-up.ps1
./scripts/db-migrate.ps1
./scripts/db-reset-demo.ps1 -ConfirmDemoReset
./scripts/test-all.ps1
```

`npm run web:e2e` checks the standalone shell and API-unavailable state. `npm run web:e2e:system` starts isolated PostgreSQL 18, applies all migrations, explicitly resets fictional data, starts the Test API, simulation worker and production Next.js server, runs the connected browser scenarios, then tears down its services. See `scripts/system-e2e.ps1` for the reproducible setup. The pinned .NET SDK is `10.0.100`. For interactive development, configure the API and worker with the documented Development/Test settings and start Next.js with `CRITICAL_ALERTS_API_URL` pointing to the local API before building or starting it. Demo reset requires the explicit confirmation switch and an approved loopback simulation database.

### Simulation container builds

The web Dockerfile installs both root and web build dependencies. Its API rewrite is compiled at build time, defaulting to `http://api:8080`. Run the API and web containers on the same Docker network with the API named or aliased `api`. For a different internal API address, rebuild with `docker build --file src/web/Dockerfile --build-arg CRITICAL_ALERTS_API_URL=http://simulation-api:8080 --tag critical-alerts-web:local .`. Changing this variable on an already-built running container does not change its proxy destination. The URL must contain only the internal service address, never credentials.

`scripts/verify-web-container.ps1` checks the built image against a separate synthetic HTTP fixture on a temporary isolated Docker network. Both CI and `scripts/test-all.ps1` run this check and remove their temporary containers afterward. Database, identity, and response settings remain subject to the existing Development/Test-only simulation guards.

### Phase 1 local toolchain baseline

- .NET SDK: `10.0.100`, exact roll-forward disabled.
- C#: `14.0`; target framework: `net10.0`.
- Node.js: `24.16.0`; npm: `11.13.0`.
- Next.js: `16.3.1`; React/React DOM: `19.2.1` (patched App Router baseline).
- PostgreSQL development/test image: `postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636`.

These are local development/test pins selected for this scaffold. They do not approve hospital identity, privacy, retention, security, clinical, escalation, communications, hosting, or production integration decisions; those remain `REQUIRES_HOSPITAL_DECISION` where documented.
