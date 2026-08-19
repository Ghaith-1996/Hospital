# Critical Clinician Alert Platform

Status: Phase 1 scaffold and local platform implemented; review pending. Phase 0 documentation has been approved by the project owner.

This workspace defines a human-confirmed, closed-loop clinician alert simulation. It is not a hospital system, not a replacement for an EHR, pager, switchboard, scheduling system, or downtime process, and it is not approved for clinical use.

Phase 1 may add only the empty modular-monolith scaffold, local PostgreSQL development/test infrastructure, health endpoints, simulation-mode web shell, repository safety checks, and CI baseline. It must not add domain behavior, database migrations, provider integrations, hospital connectors, or production configuration.

## Source and decision precedence

The user-requested mandatory rules are binding. The attached master build plan is the project baseline and work plan; its recommendations are not hospital policy and do not authorize production behavior. These Phase 0 documents translate both sources into a simulation-safe specification.

When a real workflow, escalation, privacy, security, identity, directory, communications, retention, hosting, or integration decision is missing, the documents use the exact marker `REQUIRES_HOSPITAL_DECISION`. That marker must not be replaced by an invented production default.

The current workspace is a blank, non-Git directory. Repository creation, ownership, product naming, and human role assignment remain project-owner decisions.

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
- Architecture: [system context](docs/architecture/system-context.md), [containers](docs/architecture/containers.md), [data model](docs/architecture/data-model.md), [alert state machine](docs/architecture/alert-state-machine.md), [directory integration](docs/architecture/directory-integration.md).
- Decisions: [ADRs](docs/adr/).
- Security and governance: [threat model](docs/security/threat-model.md), [data classification](docs/security/data-classification.md), [logging policy](docs/security/logging-policy.md), [production readiness gates](docs/security/production-readiness-gates.md).
- Next implementation package: [Phase 1 implementation plan](docs/superpowers/plans/2026-08-19-phase-1-scaffold-and-local-platform.md).

## Current status and review gate

Phase 0 approval is recorded for this implementation turn. The project is now executing Phase 1 within the reviewed scope; hospital decisions remain unresolved unless explicitly recorded in the documentation.

Phase 1 creates the empty solution scaffold, local PostgreSQL development container, health endpoints, test projects, simulation-mode web shell, and CI baseline. It must not add business behavior or real integrations. The implementation is ready for the Phase 1 review gate. Local Docker and Playwright browser verification remain environment actions; Phase 2 has not started.

### Phase 1 local toolchain baseline

- .NET SDK: `10.0.100`, exact roll-forward disabled.
- C#: `14.0`; target framework: `net10.0`.
- Node.js: `24.16.0`; npm: `11.13.0`.
- Next.js: `16.3.1`; React/React DOM: `19.2.1` (patched App Router baseline).
- PostgreSQL development/test image: `postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636`.

These are local development/test pins selected for this scaffold. They do not approve hospital identity, privacy, retention, security, clinical, escalation, communications, hosting, or production integration decisions; those remain `REQUIRES_HOSPITAL_DECISION` where documented.
