# Critical Alerts

A human-confirmed, closed-loop clinician alert **simulation**.

> **Simulation only.** This is not a hospital system, not a replacement for an EHR, pager, switchboard, scheduling system or downtime process, and it is not approved for clinical use. All hospital, employee, practitioner, patient, phone and clinical data is fictional; never add real PHI, contact data or credentials. Any missing real workflow, escalation, privacy, security, identity, directory, communications, retention, hosting or integration decision is marked `REQUIRES_HOSPITAL_DECISION` and must not be replaced by an invented production default. The binding rules and safety invariants are in [AGENTS.md](AGENTS.md).

## Architecture

A modular monolith: a .NET 10 API and background Worker, a Next.js web app, and PostgreSQL 18. PostgreSQL owns saved workflow state, scheduling and append-only audit; the browser holds unsaved edits only. Confirmed alerts are dispatched asynchronously through a transactional outbox to simulated channel adapters. AI, speech, SMS, voice, hospital identity and directory providers are disabled or simulated; there are no real providers or hospital integrations. See [docs/architecture](docs/architecture/containers.md).

## Requirements

.NET SDK 10.0.100 ([global.json](global.json)), Node.js 24.16.0 with npm 11.13.0, Docker, PowerShell 7. Exact package and image pins are in [local development](docs/runbooks/local-development.md).

## Run locally

```powershell
Copy-Item .env.example .env      # replace placeholders with fictional local-only values; keep .env ignored
./scripts/dev-up.ps1             # local PostgreSQL via Docker Compose
./scripts/db-migrate.ps1
./scripts/db-reset-demo.ps1 -ConfirmDemoReset
./scripts/dev-down.ps1           # stop PostgreSQL when finished
```

Start the API, Worker and web app in separate terminals, and use the fictional identity switcher. Environment switches (`DevelopmentAuthentication__Enabled`, `SimulationResponses__Enabled`, `SimulationDispatch__Enabled`, `SimulationEscalation__Enabled`), ports, identities and routes are in [local development](docs/runbooks/local-development.md).

## Tests

- `./scripts/test-all.ps1`: full local gate (restore, format, build, backend tests with real PostgreSQL via Testcontainers, dependency/OpenAPI/sensitive-data checks, web tests, typecheck, lint, browser flows, container builds).
- `npm run web:e2e`: standalone browser shell checks.
- `npm run web:e2e:system`: connected scenarios against isolated PostgreSQL, API, Worker and production web build.
- CI runs the same gate in [.github/workflows/ci.yml](.github/workflows/ci.yml).

## Repository structure

```text
src/backend/   .NET solution: Api, Application, Domain, Infrastructure, Worker, Connector (placeholder)
src/web/       Next.js App Router web app
tests/         backend test projects and browser e2e
scripts/       dev, database, test and verification scripts
docs/          product, architecture, ADRs, security, runbooks, API contract, history
```

## Documentation

- Rules: [AGENTS.md](AGENTS.md); [definition of done](docs/product/definition-of-done.md); [phase approval evidence](docs/product/phase-approval-evidence.md).
- Product: [product decisions](docs/product/product-decisions.md), [workflow](docs/product/workflow.md), [demo-data rules](docs/product/demo-data-rules.md), [terminology](docs/product/terminology.md).
- Architecture: [system context](docs/architecture/system-context.md), [containers](docs/architecture/containers.md), [data model](docs/architecture/data-model.md), [alert state machine](docs/architecture/alert-state-machine.md), [alert drafting](docs/architecture/alert-drafting.md), [directory integration](docs/architecture/directory-integration.md), [recipient selection and review](docs/architecture/recipient-selection-and-review.md), [simulated dispatch](docs/architecture/simulated-dispatch.md), [observability](docs/architecture/observability.md), [speech and AI suggestions](docs/architecture/speech-and-ai-suggestions.md).
- Decisions: [ADRs](docs/adr/).
- Security: [threat model](docs/security/threat-model.md), [data classification](docs/security/data-classification.md), [logging policy](docs/security/logging-policy.md), [production readiness gates](docs/security/production-readiness-gates.md), [safety invariant traceability](docs/security/safety-invariant-traceability.md).
- Runbooks: [local development](docs/runbooks/local-development.md), [notification provider outage](docs/runbooks/notification-provider-outage.md), [directory synchronization failure](docs/runbooks/directory-sync-failure.md), [database restore test](docs/runbooks/database-restore-test.md).
- API contract: [docs/api/openapi.json](docs/api/openapi.json).
- Phase 11 (speech and AI suggestions): [design](docs/superpowers/specs/2026-09-19-phase-11-speech-ai-suggestions-design.md), [plan](docs/superpowers/plans/2026-09-19-phase-11-speech-ai-suggestions.md), [verification record](docs/superpowers/phase11-verification.md); `scripts/run-ai-evaluation.ps1` runs fictional deterministic evaluation.
- History: [development history](docs/archive/development-history.md).
