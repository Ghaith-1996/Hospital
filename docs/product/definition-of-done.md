# Phase Definition of Done

## Phase 0: specification and repository rules

Phase 0 is done only when all of the following are true:

- [ ] README and AGENTS.md state scope, authority, phase gate, and safety invariants.
- [ ] Product decisions and workflow documents identify every missing hospital decision with `REQUIRES_HOSPITAL_DECISION`.
- [ ] Simulation-only assumptions are explicitly labelled and cannot be mistaken for production rules.
- [ ] Original source, transcription, structured suggestions, approved content, critical-number confirmation, and recipient confirmation are specified separately.
- [ ] Delivered, opened, acknowledged, and responsibility accepted are defined as separate states.
- [ ] Architecture documents describe the modular monolith, PostgreSQL boundary, outbox, directory integration boundary, and state machine.
- [ ] ADRs record the major Phase 0 architectural and safety choices.
- [ ] ADRs remain proposed/pending until the Phase 0 approval record is completed; mandatory user safety rules are clearly distinguished from project-owner and hospital decisions.
- [ ] Threat model, data classification, logging policy, and production readiness gates exist.
- [ ] Simulation fixtures permit fictional `555` phone values only, and any future real test endpoints are explicitly outside simulation and marked `REQUIRES_HOSPITAL_DECISION`.
- [ ] The simulation contract defines deterministic fictional roles and test mechanics without creating production policy.
- [ ] Delivery-state semantics distinguish supported, pending/not observed, occurred, failed, and `NotApplicable` states.
- [ ] The Phase 1 implementation plan names files, interfaces, tests, commands, and review gates without adding code to the repository.
- [ ] The Phase 1 plan has no cross-task test dependency ambiguity, uses exact dependency/image pinning, and distinguishes implementation tasks from the final review gate.
- [ ] A human reviews and approves the Phase 0 package.

## Phase 0 non-goals

The following must not be present as Phase 0 implementation work:

- .NET solution or project files.
- Next.js application files.
- Database migrations or schema scripts.
- Docker Compose or runtime containers.
- Notification, speech, AI, directory, identity, or hospital integrations.
- Production policy values, escalation timings, real contact routes, or real data.

## Later phase gate pattern

Every later phase must state:

1. Files changed.
2. Architectural decisions.
3. Commands run and results.
4. Tests added and results.
5. Known limitations and unresolved human decisions.
6. Human actions required.
7. Proposed commit message.
8. A clear stop for review before the next phase.

## Phase 1: scaffold and local platform

Phase 1 is ready for human review only when the following are true:

- [x] The modular-monolith project graph, pinned toolchain, and empty project shells build successfully.
- [x] The simulation-mode web shell has no alert behavior or dispatch control.
- [x] Liveness and safe database-readiness endpoints exist without domain or notification behavior.
- [x] Local PostgreSQL and Testcontainers infrastructure use the pinned PostgreSQL 18 image and no Phase 1 schema.
- [x] Repository safety scanning, CI configuration, and dependency audits are present.
- [x] No secrets, real contact data, real clinical data, migrations, provider calls, or hospital production configuration were added.
- [x] Real PostgreSQL/Testcontainers verification is run after Docker Desktop is started by the project owner.
- [x] Playwright smoke verification is run after the pinned browser binary is installed in the local environment.
- [x] The project owner reviews this phase and explicitly approves or requests corrections before Phase 2.

## Phase 2: database and domain foundation

Phase 2 is ready for human review only when the following are true:

- [x] Domain entities, alert state machine, and safety constraints exist with unit tests.
- [x] EF Core mappings and a PostgreSQL 18 migration exist.
- [x] An empty database migrates and fictional demo seed data loads in Development/Test.
- [x] Demo reset refuses Staging and Production.
- [x] Organization isolation, optimistic concurrency, outbox atomicity, and idempotency uniqueness are tested against real PostgreSQL.
- [x] `user_roles` uniqueness is `UNIQUE (organization_id, user_id, role_id)`.
- [x] `alert_field_confirmations` stores one canonical confirmation per alert, draft version, and field identifier.
- [x] Acknowledgement and responsibility acceptance remain separate records.
- [x] No alert APIs, authentication, provider adapters, AI features, or hospital integrations were added.
- [x] The project owner reviewed this phase, requested uniqueness-constraint closure, and those corrections are included before Phase 3.
