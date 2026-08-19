# ADR-0002: Use PostgreSQL 18 with EF Core 10/Npgsql 10

- Status: Proposed simulation baseline; pending Phase 0 approval. Production database operations require approval.
- Date: 2026-08-19.
- Deciders: `REQUIRES_PROJECT_OWNER_DECISION`.

## Context

The workflow needs relational constraints, organization scoping, optimistic concurrency, append-only event records, reliable outbox leasing, idempotency uniqueness, and deterministic due-work queries. These behaviors need to be exercised against the same relational engine used by the MVP.

## Decision

Use PostgreSQL 18 with EF Core 10 and Npgsql 10. Use migrations and real PostgreSQL integration tests through Testcontainers. Do not use an in-memory provider as a substitute for relational behavior.

## Consequences

Positive:

- Foreign keys, unique constraints, indexes, transaction isolation, and row-level concurrency are testable.
- Local Docker development is reproducible.
- The data model supports append-only timeline and outbox patterns.

Trade-offs:

- Tests require a working container runtime.
- Migration compatibility and forward-only rollout need explicit review.
- Production backup, restore, encryption, roles, residency, retention, and sizing cannot be inferred from this ADR.

## Guardrails

- Every organization-owned table has an intentional organization boundary.
- Every foreign key has intentional delete behavior.
- Production migrations use a migration-specific identity; runtime identities cannot create/drop schemas.
- Normal application code does not hard-delete alerts or audit events.
- Sensitive data protection is abstracted and tested; keys are never hardcoded.

## Not decided here

Production tier/size, region, high availability, backups, retention, legal hold, encryption/key management, access review, and disaster recovery objectives are `REQUIRES_HOSPITAL_DECISION`.
