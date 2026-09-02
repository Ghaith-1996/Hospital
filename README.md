# Critical Clinician Alert Platform

Status: The active work is the approved frontend-only prototype redesign described in `docs/superpowers/specs/2026-08-30-frontend-prototype-redesign-design.md`. It may implement the nine fictional operator/doctor UI states with local mock state only. Phase 7 remains the backend baseline. No backend doctor responses, live delivery behavior, escalation processing, real providers, production identity, hospital integration, or real data are authorized in this phase.

This workspace defines a human-confirmed, closed-loop clinician alert simulation. The current frontend-only prototype phase is local and visibly simulated. It is not a hospital system, not a replacement for an EHR, pager, switchboard, scheduling system, or downtime process, and it is not approved for clinical use.

Phase 4 provides a fictional practitioner directory, CSV import adapter, validation/preview, and searchable directory UI. Phase 5 adds protected typed simulation alert drafting and SBAR confirmation. Phase 6 adds manual fictional-recipient selection, protected approved-message content, exact review, and idempotent human confirmation that creates an identifier-only outbox item. Phase 7 adds a Development/Test-only simulation worker, typed local channel adapters, deterministic provider-event scenarios, bounded retry, lease recovery, and safe delivery-status projection as the backend baseline. The approved frontend-only prototype phase may add local mock UI for doctor response, live-detail, and demo-escalation states as `SIMULATION_ONLY_ASSUMPTION` behavior. No backend doctor responses, live delivery behavior, escalation processing, real providers, hospital connectors, SCIM, Graph, FHIR, AI features, Entra SSO, production identity, or real data are authorized.

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

Phase 0 through Phase 5 approval are recorded, and the reviewed baselines are tagged through `phase-5`. The Phase 6 recipient-selection boundary is the implemented predecessor, and the Phase 7 simulation-only worker plan remains the backend baseline. The active work is the approved frontend-only prototype redesign for local, visibly simulated UI only. Hospital directory connections, production recipient eligibility, freshness limits, channel mappings, provider contracts, callback authentication, confirmer role mapping, real doctor-response workflow authority, and real escalation policy remain `REQUIRES_HOSPITAL_DECISION`.

Phase 4 added a fictional CSV adapter over the shared practitioner directory model, strict validation/preview, duplicate detection, source-owned reconciliation, freshness filtering, and a searchable directory UI. Phase 5 adds protected typed source/SBAR drafts, optimistic draft versions, critical-field confirmation, and a compose UI. Phase 6 preserves the original source separately from SBAR and approved content, replaces recipients as one exact versioned set, shows safe directory evidence, and confirms only the exact reviewed version. Phase 7 consumes only that durable identifier-only outbox row: the worker leases it, reloads organization-scoped durable data, invokes typed deterministic simulation adapters, records safe delivery attempts/events, and completes, retries, or visibly fails the work. Simulation dispatch is fail-closed outside Development/Test and has no network/provider side effects.

The Phase 6 specification is [recipient selection and review](docs/architecture/recipient-selection-and-review.md), and the Phase 7 boundary is [simulated dispatch](docs/architecture/simulated-dispatch.md).

### Local verification

Create an ignored `.env` from `.env.example` with fictional local-only values, then run:

```powershell
Copy-Item .env.example .env
./scripts/dev-up.ps1
./scripts/db-migrate.ps1
./scripts/db-reset-demo.ps1
./scripts/test-all.ps1
```

The Playwright smoke test starts the web shell on `127.0.0.1:3100` so it does not reuse another application on port 3000. The pinned .NET SDK is `10.0.100`. To use the development identity switcher locally, start `CriticalAlerts.Api` on `http://127.0.0.1:5080` and set `CRITICAL_ALERTS_API_URL` for the Next.js rewrite.

### Phase 1 local toolchain baseline

- .NET SDK: `10.0.100`, exact roll-forward disabled.
- C#: `14.0`; target framework: `net10.0`.
- Node.js: `24.16.0`; npm: `11.13.0`.
- Next.js: `16.3.1`; React/React DOM: `19.2.1` (patched App Router baseline).
- PostgreSQL development/test image: `postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636`.

These are local development/test pins selected for this scaffold. They do not approve hospital identity, privacy, retention, security, clinical, escalation, communications, hosting, or production integration decisions; those remain `REQUIRES_HOSPITAL_DECISION` where documented.
