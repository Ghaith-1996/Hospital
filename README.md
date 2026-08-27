# Critical Clinician Alert Platform

Status: Phase 5 simulation-only alert drafting is approved, tagged `phase-5`, and published. Phase 6 recipient selection and final-review planning is underway; no Phase 6 application code has been added.

This workspace defines a human-confirmed, closed-loop clinician alert simulation. It is not a hospital system, not a replacement for an EHR, pager, switchboard, scheduling system, or downtime process, and it is not approved for clinical use.

Phase 4 provides a fictional practitioner directory, CSV import adapter, validation/preview, and searchable directory UI. Phase 5 adds protected typed simulation alert drafting and SBAR confirmation. Phase 6 is being planned for manual fictional-recipient selection, exact review, and idempotent human confirmation. Provider integrations, hospital connectors, SCIM, Graph, FHIR, AI features, Entra SSO, production identity, and outbox processing remain out of scope.

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

Phase 0 through Phase 5 approval are recorded, and the reviewed baselines are tagged through `phase-5`. Phase 6 planning is ready for project-owner review before implementation begins. Hospital directory connections remain `REQUIRES_HOSPITAL_DECISION`.

Phase 4 added a fictional CSV adapter over the shared practitioner directory model, strict validation/preview, duplicate detection, source-owned reconciliation, freshness filtering, and a searchable directory UI. Phase 5 adds protected typed source/SBAR drafts, optimistic draft versions, critical-field confirmation, and a compose UI. The original operator-entered source content is persisted separately from the structured SBAR representation and is never silently overwritten by normalization. No alert can reach `DispatchQueued` through any Phase 5 endpoint. The Phase 6 plan adds no executable behavior and keeps provider integrations, hospital connectors, AI features, outbox processing, delivery attempts, and production SSO out of scope.

The Phase 6 specification is [recipient selection and review](docs/architecture/recipient-selection-and-review.md), and its executable work plan is [Phase 6 recipient selection and review](docs/superpowers/plans/2026-08-27-phase-6-recipient-selection-and-review.md).

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
