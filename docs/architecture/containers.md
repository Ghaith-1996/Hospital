# Containers and Module Boundaries

Status: Phase 8.5 active simulation topology, retaining the Phase 0 modular-monolith boundaries. Future integration ports below are not active providers. Saved workflow state is authoritative in PostgreSQL; the browser holds unsaved edits only.

## Deployable processes

| Container/process | Responsibility | Must not do |
|---|---|---|
| `web` | Next.js App Router operator, practitioner, and admin experiences; display `SIMULATION MODE`; call versioned API. | Enforce authorization by itself, select recipients automatically, or hold encryption keys. |
| `CriticalAlerts.Api` | Backend development session, authorization, organization-scoped commands/queries, exact review/confirmation, practitioner responses, lifecycle, health, and problem details. No external callbacks. | Call providers before durable confirmation or accept browser claims as authority. |
| `CriticalAlerts.Worker` | Lease outbox messages, create delivery attempts, normalize local simulated events, perform bounded simulation retry, and recover durable jobs. | Diagnose, assign urgency, choose recipients, contact a real provider, or run Phase 9 escalation automation. |
| `CriticalAlerts.Connector` | Future hospital-side directory/scheduling connector boundary. | Connect to a hospital system without approved specifications and contracts. |
| PostgreSQL 18 | Durable state, organization boundaries, concurrency tokens, append-only timeline data, outbox/inbox/idempotency. | Serve as a public interface or receive unrestricted patient data. |
| Simulated provider adapters | Deterministic Development/Test delivery and response scenarios. | Contact real endpoints or accept simulation configuration outside Development/Test. |

## Internal modules

The modular monolith remains one deployable codebase with explicit internal boundaries:

1. Identity and authorization.
2. Directory.
3. Alert drafting and provenance.
4. Alert workflow and state transitions.
5. Notification orchestration.
6. Acknowledgement and responsibility.
7. Escalation policy references only; automated execution remains outside Phase 8.5.
8. Audit and PHI-safe observability.
9. Integrations and provider ports.

The domain module has no dependency on ASP.NET Core, EF Core, Azure, HTTP, UI, or provider SDKs. Application commands validate authorization, organization scope, version, and invariants. Infrastructure implements persistence and provider ports. API and Worker compose those modules.

## Critical write transaction

The `ConfirmAndDispatchAlert` operation is the most important transaction boundary:

```text
validate authenticated operator and organization scope
validate exact draft version and required field confirmations
validate exact manually selected active recipients and channels
persist approved alert version
persist recipients
transition alert to DispatchQueued
append audit event
append AlertDispatchRequested outbox message containing identifiers only
commit once
```

No provider call occurs inside this transaction. The worker acts only after the durable outbox message exists.

## External interfaces

All providers are ports with simulated implementations first:

- notification channel dispatch and status normalization;
- transcription;
- alert structuring suggestions;
- identity and directory;
- on-call scheduling;
- sensitive-data protection;
- queue/message bus.

The provider contract must not pass full clinical content to SMS or voice adapters by default. It must pass a generic wake-up message or a secure-message reference, an opaque provider endpoint reference (never a raw phone number or email address), an idempotency key, and a correlation ID.

## Runtime configuration

Configuration must distinguish `Development`, `Test`, `Staging`, and `Production`. Development authentication and simulated providers fail closed outside Development/Test. Any production endpoint, policy, identity, data, region, retention, or provider configuration not approved by the hospital is `REQUIRES_HOSPITAL_DECISION`.

## Historical Phase 1 boundary

Phase 1 may create empty project shells, health endpoints, local PostgreSQL composition, and test infrastructure. It must not implement alert commands, domain behavior, migrations, provider calls, business UI, or integration logic.

## Connected browser boundary

Next.js proxies `/api/v1` to the configured internal API address. This destination is compiled at web build time; the container default is `http://api:8080`. Development identity selection posts only a server-listed handle and reloads the server principal; it cannot grant a role from browser state. The authorized simulation location endpoint supplies site/department identifiers. Draft, directory, review, inbox, live-status, and lifecycle screens use existing Phase 4–8 APIs.

The system harness starts isolated PostgreSQL 18, migrations/demo reset, API, worker, production web and Chromium, then tears down its resources. No real provider or hospital connection is needed. The repository remains public and all test content is fictional. Missing production decisions remain `REQUIRES_HOSPITAL_DECISION`.
