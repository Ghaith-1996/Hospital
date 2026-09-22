# Phase 9 implementation plan

Authority: project-owner Phase 9 attachment and instruction to implement. Spec: `../specs/2026-09-22-phase-9-escalation-design.md`. Work sequentially on `feature/alerts-and-escalations`; commit isolated tested slices. No Phase 10.

For each task list exact files before edits, add focused tests, record expected RED, implement, run focused and project regression tests, inspect sensitive-data boundaries, commit. Progress and evidence: `../phase9-verification.md`.

1. **Specification and boundary:** AGENTS, this plan, spec, verification record.
2. **Exact review and confirmation:** application review contracts, review service, policy snapshot domain/persistence, connected review/client; API tests for exact policy/revision/evidence, replay and rollback. Persistence prerequisites are included here so the approval boundary is independently testable before any worker exists.
3. **Run domain:** extend existing EscalationRun and enums; tests for exact version, UTC, stop precedence, pause/resume, expedite and exhaustion.
4. **Persistence:** additive run/event migration and mappings, database immutability/uniqueness/scoping/append-only tests. No historical backfill.
5. **Scheduling and concurrency:** database-clock processor, durable claim/expired recovery, shared alert lock; PostgreSQL tests.
6. **Activation:** only stored plan recipients, source EscalationPolicy, unchanged version, one step/event activation; PostgreSQL tests.
7. **Outbox:** strict new dispatch payload and selected-ID processing via existing channels; no original redispatch; existing/new dispatch tests.
8. **Stop and overrides:** durable response/lifecycle precedence, Pause/Resume service/endpoints, role/environment guards, idempotency/audit tests.
9. **Live projection:** policy/run/events/provenance/control permissions and safe fallback; API isolation and non-disclosure tests.
10. **Connected frontend:** live panel/controls and review approval binding, transient state only, double-submit/retry/poll cleanup tests.
11. **System proof:** extend real system harness/scenarios D–I, restart and two-worker concurrency.
12. **Full verification and documentation:** format/build/tests/typecheck/lint/OpenAPI/security/storage/Playwright/system/container/fresh migration/reset checks; review and stop for owner review.

Shared interfaces: task 2's immutable snapshot is the sole scheduling/activation authority in tasks 3–7; tasks 5–8 share alert lock ordering with Phase 8 responses/lifecycle; task 7 dispatches only task 6's new selection IDs; tasks 9–10 expose safe projections and server control permissions. Review hash includes exact version and evidence; retries retain the same hash/key. Sequential slices may introduce additive persistence prerequisites before their later exhaustive verification task.
