# Local development

## Purpose
Run the fictional communication simulation and reproduce Phase 10 verification.

## Scope
Development/Test only, pinned .NET 10.0.100 (C# 14, net10.0, exact roll-forward disabled), Node 24.16.0 / npm 11.13.0, Next.js 16.3.6, React 19.2.1, Playwright 1.60.0, Docker and PostgreSQL `postgres:18.4@sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636`. These are local development/test pins, not hospital approvals. No hospital integration or real provider.

## Detection
A missing dependency, failed startup, or non-successful readiness response prevents connected work. Liveness only establishes that the API process responds.

## Safety impact
Demo reset deletes the selected allowed local simulation database. It is never a recovery technique for an existing alert. All entered data must be fictional.

## Immediate actions
From the repository root in PowerShell:
```powershell
Copy-Item .env.example .env
./scripts/dev-up.ps1        # starts the compose PostgreSQL service
./scripts/db-migrate.ps1
./scripts/db-reset-demo.ps1 -ConfirmDemoReset
./scripts/test-all.ps1
./scripts/dev-down.ps1      # stops the compose PostgreSQL service; volumes are retained
```
Before dev-up, privately replace the local placeholders and generate the local protection key as described in .env.example. Keep .env ignored. Set ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT to Development. Configure ConnectionStrings__CriticalAlerts privately for the loopback simulation database and CRITICAL_ALERTS_DATA_PROTECTION_KEY in each API/worker terminal; never paste either value into evidence.

## What NOT to do
Do not use patient or employee data, commit .env, disable authentication, enable a real provider, or reset a database containing evidence you intend to retain.

## Diagnosis
Check Docker availability, the pinned toolchain, the compose PostgreSQL health state, and API health:
```powershell
Invoke-RestMethod http://127.0.0.1:5080/health/live
Invoke-RestMethod http://127.0.0.1:5080/health/ready
```
A ready failure is a database dependency issue; a delayed worker alone must not make readiness fail. Review only the fixed operational log categories.

## Recovery
Start each long-running process in its own terminal after private environment configuration:
```powershell
dotnet run --project src/backend/CriticalAlerts.Api
$env:SimulationDispatch__Enabled = "true"
$env:SimulationEscalation__Enabled = "true"
dotnet run --project src/backend/CriticalAlerts.Worker --no-launch-profile
```
The API Development settings enable fictional sessions/responses. Set DevelopmentAuthentication__Enabled=true and SimulationResponses__Enabled=true explicitly when running in Test. The worker uses DOTNET_ENVIRONMENT=Development/Test; both simulation switches must be explicit. In a separate web terminal:
```powershell
$env:CRITICAL_ALERTS_API_URL = "http://127.0.0.1:5080"
npm ci --no-audit --no-fund
npm ci --prefix src/web --no-audit --no-fund
npm --prefix src/web run dev
```
Simulation switches (all default disabled, fail closed outside Development/Test): `DevelopmentAuthentication__Enabled` (server-controlled fictional sessions), `SimulationResponses__Enabled` (practitioner responses), `SimulationDispatch__Enabled` (worker dispatch) and `SimulationEscalation__Enabled` (worker escalation; requires `SimulationDispatch__Enabled`). Seeded `DEMO-9` uses one 60-second step with SecureMessage backups; these are fictional assumptions (only `scripts/system-e2e.ps1` seeds a shorter step, 15 seconds by default via `-EscalationStepDelaySeconds`, by setting `SimulationEscalation__DemoStepDelaySeconds` for `database reset-demo`, so the escalation E2E tests do not wait a real minute), and expired or missing on-call evidence yields an explicitly empty reviewed step, then manual fallback. Speech and AI assistance flags are in [speech and AI suggestions](../architecture/speech-and-ai-suggestions.md).

Use the fictional identity switcher: Jordan (operator), Riley (practitioner), Morgan (administrator), Avery Auditor (audit reader). Avery cannot perform operator actions. Routes: `/alerts/new` (draft and compose), `/alerts/[id]/live` (polls durable delivery and response state), `/my-alerts` (practitioner inbox via the backend user-to-practitioner mapping), directory search with CSV preview/apply, and `/alerts` (opens an alert by server ID; there is no operator-wide list endpoint). Reports and Settings are unavailable. Unsaved edits live in browser memory only; refresh recovers backend-saved content.

## Verification
Complete a fictional workflow and inspect connected live status. As Avery, open /admin/audit and confirm bounded pages. Run test-all for the complete automated gate. `npm run web:e2e` checks the standalone shell and API-unavailable state; `npm run web:e2e:system` (`scripts/system-e2e.ps1`) starts isolated PostgreSQL 18, applies migrations, resets fictional data, starts the Test API, worker and production web server, runs the connected browser scenarios and tears everything down. `scripts/system-e2e.ps1 -EnableAssistance -TestPattern 'Phase11:'` runs the opt-in speech and AI suggestion scenarios with explicit simulation flags. Container build and verification notes are in [containers](../architecture/containers.md#simulation-container-builds). The restore exercise requires an explicitly set Development/Test environment and -ConfirmRestoreTest; it owns a separate disposable source and restore container.

## Evidence to preserve
Commit/branch, command exit codes, test counts, health categories, opaque correlation IDs and restore summary flags only. No credentials, workflow payload, raw logs, database dumps or clinical screenshots.

## Exit criteria
API liveness/readiness pass, web connects to API, workers process the fictional workflow, and relevant tests pass. Stop web/API/worker with Ctrl+C, then run `./scripts/dev-down.ps1` (or docker compose down). This preserves the local volume. Volume deletion and demo reset require deliberate local-data disposal.

## Production decisions
REQUIRES_HOSPITAL_DECISION: support/on-call ownership, production authentication mapping, deployment, recovery authority and all real fallback routes.
