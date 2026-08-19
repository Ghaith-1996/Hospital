# ADR-0001: Use a Modular Monolith

- Status: Proposed simulation baseline; pending Phase 0 approval. Production deployment topology remains subject to review.
- Date: 2026-08-19.
- Deciders: `REQUIRES_PROJECT_OWNER_DECISION`.

## Context

The first system has tightly coupled workflow invariants: human confirmation, draft versioning, recipient selection, durable outbox creation, delivery status, response semantics, escalation, and audit. Splitting these concerns into independently deployed services before the workflow is proven would add operational and consistency costs.

## Decision

Use a modular monolith with explicit internal boundaries and separate deployable processes for:

- `CriticalAlerts.Api`.
- `CriticalAlerts.Worker`.
- `CriticalAlerts.Connector` as a future, separately deployable hospital-side boundary.
- `web` as the Next.js interface.

Keep domain rules independent of HTTP, EF Core, Azure, UI, and provider SDKs. Use ports/interfaces for external providers and simulation implementations first.

## Consequences

Positive:

- One durable transaction can cover approved alert state, recipients, audit, and outbox creation.
- Local setup and integration testing are smaller.
- Module boundaries preserve a future extraction path.
- Simulation can run without hospital services.

Trade-offs:

- The monolith needs architecture tests and dependency rules to prevent module leakage.
- API and worker still require careful concurrency, leasing, and idempotency design.
- Scaling and deployment independently are limited until extraction is justified.

## Guardrails

- No module may bypass authorization or organization scope.
- The worker cannot create or alter approved content.
- The UI cannot be the only enforcement point.
- External adapters remain behind interfaces.

## Not decided here

Azure hosting, region, private networking, service-bus topology, availability targets, and production decomposition are `REQUIRES_HOSPITAL_DECISION` or `REQUIRES_PROJECT_OWNER_DECISION` as applicable.
