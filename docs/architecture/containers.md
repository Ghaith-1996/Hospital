# Containers and Module Boundaries

Active simulation topology over the modular-monolith boundaries. Future integration ports below are not active providers. Saved workflow state is authoritative in PostgreSQL; the browser holds unsaved edits only.

## Deployable processes

| Container/process | Responsibility | Must not do |
|---|---|---|
| `web` | Next.js App Router operator, practitioner, and admin experiences; display `SIMULATION MODE`; call versioned API. | Enforce authorization by itself, select recipients automatically, or hold encryption keys. |
| `CriticalAlerts.Api` | Backend development session, authorization, organization-scoped commands/queries, exact review/confirmation, practitioner responses, lifecycle, health, and problem details. No external callbacks. | Call providers before durable confirmation or accept browser claims as authority. |
| `CriticalAlerts.Worker` | Lease outbox messages, run simulation escalation (when enabled), create delivery attempts, normalize local simulated events, perform bounded simulation retry, and recover durable jobs. | Diagnose, assign urgency, choose recipients, or contact a real provider. It runs the escalation processor only when `SimulationEscalation__Enabled=true` together with `SimulationDispatch__Enabled=true` in Development/Test, and only for human-approved exact future DEMO recipients. |
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
7. Escalation: simulation-only DEMO policy and approved future-recipient snapshots, executed by the worker when enabled.
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

## Connected browser boundary

Next.js proxies `/api/v1` to the configured internal API address. This destination is compiled at web build time; the container default is `http://api:8080`. Development identity selection posts only a server-listed handle and reloads the server principal; it cannot grant a role from browser state. The authorized simulation location endpoint supplies site/department identifiers. Draft, directory, review, inbox, live-status, and lifecycle screens use existing Phase 4–8 APIs.

The browser is never the source of truth for alert workflow state (simulation design rule, Phase 8.5). Unsaved forms live in memory only, with unload, link, identity-switch and Chromium history navigation warnings; stale saves retain the unsaved buffer until the operator explicitly discards it and loads the server version. No workflow content is written to `localStorage` or `sessionStorage`; `scripts/verify-web-storage-safety.ps1` enforces this for the active routes, features and API clients. An API failure never produces a local success transition. An uncertain confirmation or response keeps its exact in-memory idempotency key and payload for retry; a successful mutation followed by a failed read retries the read rather than generating a new command key. These keys do not survive a browser restart, and the backend still rejects invalid duplicate transitions.

The system harness starts isolated PostgreSQL 18, migrations/demo reset, API, worker, production web and Chromium, then tears down its resources. No real provider or hospital connection is needed. The repository remains public and all test content is fictional. Missing production decisions remain `REQUIRES_HOSPITAL_DECISION`.

## Speech and AI suggestions

All features default disabled and production decisions remain `REQUIRES_HOSPITAL_DECISION`. Providers run synchronously in the API after a short durable idempotency claim; external work holds no database transaction or alert lock. The worker never generates suggestions. Default API containers have both flags off. Local opt-in environment settings and WAV transport constraints are documented in speech-and-ai-suggestions.md. Azure credentials are server-only and are not image arguments, public frontend variables, compose defaults or checked-in values. Production environment selection disables assistance regardless of credentials. Browser typing and the existing simulation dispatch workflow remain available with assistance disabled.

## Simulation container builds

The web Dockerfile installs both root and web build dependencies. Its API rewrite is compiled at build time, defaulting to `http://api:8080`; run the API and web containers on the same Docker network with the API named or aliased `api`. For another internal address, rebuild: `docker build --file src/web/Dockerfile --build-arg CRITICAL_ALERTS_API_URL=http://simulation-api:8080 --tag critical-alerts-web:local .`. Changing the variable on a running container does not change its proxy destination. The URL must contain only the internal service address, never credentials.

`scripts/verify-web-container.ps1` checks the built image against a separate synthetic HTTP fixture on a temporary isolated Docker network and removes its containers afterward; CI and `scripts/test-all.ps1` both run it. Database, identity and response settings remain subject to the Development/Test-only simulation guards.

## Real communication adapters (ACS SMS)

- **Worker:** can replace the simulated SMS channel with the test-number-only `AzureCommunicationServicesSmsChannel` when `Communications:Sms:Provider=AzureCommunicationServices`. Invalid or Production configuration refuses startup.
- **API:** exposes `POST /api/v1/webhooks/communications/acs-sms` only when `Communications:Webhooks:EventGrid:Enabled=true` outside Production. The route is authenticated by the `EventGridWebhook` JWT scheme and excluded from the client OpenAPI contract.

See [real communication adapters](real-communication-adapters.md).
