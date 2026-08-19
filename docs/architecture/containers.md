# Containers and Module Boundaries

Status: Phase 0 modular-monolith design. The containers described here are planned boundaries; Phase 0 creates no application projects.

## Deployable processes

| Container/process | Responsibility | Must not do |
|---|---|---|
| `web` | Next.js App Router operator, practitioner, and admin experiences; display `SIMULATION MODE`; call versioned API. | Enforce authorization by itself, select recipients automatically, or hold encryption keys. |
| `CriticalAlerts.Api` | Authentication boundary, authorization, commands/queries, review/confirmation endpoints, provider webhook endpoints, health endpoints, and problem details. | Call providers before durable confirmation or accept browser claims as authority. |
| `CriticalAlerts.Worker` | Lease outbox messages, create delivery attempts, normalize provider events, retry within policy, evaluate versioned escalation work, and recover durable jobs. | Diagnose, assign urgency, choose final recipients, or stop escalation autonomously. |
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
7. Escalation.
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

## Phase 1 boundary

Phase 1 may create empty project shells, health endpoints, local PostgreSQL composition, and test infrastructure. It must not implement alert commands, domain behavior, migrations, provider calls, business UI, or integration logic.
