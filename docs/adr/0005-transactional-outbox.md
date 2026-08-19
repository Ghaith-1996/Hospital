# ADR-0005: Use a Transactional Outbox for Dispatch Requests

- Status: Proposed simulation baseline; pending Phase 0 approval. Production queue choice requires approval.
- Date: 2026-08-19.
- Deciders: `REQUIRES_PROJECT_OWNER_DECISION`.

## Context

The operator's confirmation must not appear successful while the delivery request is lost. Calling providers inside the confirmation request creates partial-failure and retry ambiguity. Publishing a message before the database transaction commits creates the opposite inconsistency.

## Decision

Persist the approved alert, selected recipients, `DispatchQueued` state, audit event, and `AlertDispatchRequested` outbox message in one PostgreSQL transaction. The worker leases outbox messages, expands them into delivery attempts, calls simulated/approved providers, stores normalized results, schedules bounded retries or escalation, and marks the outbox message complete only after durable state is written.

Outbox payloads contain identifiers and control metadata only. Delivery attempts use stable idempotency keys. Provider events use inbox uniqueness and replay protection.

## Consequences

Positive:

- Confirmation and dispatch intent are durable together.
- Browser closure or API restart does not lose a confirmed alert.
- Retries and provider callbacks are auditable and idempotent.

Trade-offs:

- Worker leasing, retries, poison messages, and recovery need tests and operations.
- Delivery is asynchronous and the UI must show queued versus delivered.
- Queue/bus choice and capacity planning remain deployment decisions.

## Guardrails

- No provider call occurs before the outbox message is committed.
- Duplicate outbox processing cannot create duplicate delivery attempts.
- All failure states are visible to the operator.
- Escalation uses the approved policy version only.
- No clinical body is copied into outbox or ordinary logs.

## Not decided here

Production queue service, lease durations, retry schedule, throughput, dead-letter operations, retention, alerting, and service-level objectives are `REQUIRES_HOSPITAL_DECISION` and `REQUIRES_PROJECT_OWNER_DECISION` as applicable.
