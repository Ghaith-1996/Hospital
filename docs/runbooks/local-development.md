# Local development

## Purpose
Run the fictional communication simulation and reproduce Phase 10 verification.

## Scope
Development/Test only, pinned .NET 10.0.100, Node 24.16.0, Docker and pinned PostgreSQL 18.4. No hospital integration or real provider.

## Detection
A missing dependency, failed startup, or non-successful readiness response prevents connected work. Liveness only establishes that the API process responds.

## Safety impact
Demo reset deletes the selected allowed local simulation database. It is never a recovery technique for an existing alert. All entered data must be fictional.

## Immediate actions
From the repository root in PowerShell:
```powershell
Copy-Item .env.example .env
./scripts/dev-up.ps1
./scripts/db-migrate.ps1
./scripts/db-reset-demo.ps1 -ConfirmDemoReset
./scripts/test-all.ps1
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
Use the fictional identity switcher: Jordan (operator), Riley (practitioner), Morgan (administrator), Avery Auditor (audit reader). Avery cannot perform operator actions.

## Verification
Complete a fictional workflow and inspect connected live status. As Avery, open /admin/audit and confirm bounded pages. Run test-all for the complete automated gate. The restore exercise requires an explicitly set Development/Test environment and -ConfirmRestoreTest; it owns a separate disposable source and restore container.

## Evidence to preserve
Commit/branch, command exit codes, test counts, health categories, opaque correlation IDs and restore summary flags only. No credentials, workflow payload, raw logs, database dumps or clinical screenshots.

## Exit criteria
API liveness/readiness pass, web connects to API, workers process the fictional workflow, and relevant tests pass. Stop web/API/worker with Ctrl+C, then run docker compose down. This preserves the local volume. Volume deletion and demo reset require deliberate local-data disposal.

## Production decisions
REQUIRES_HOSPITAL_DECISION: support/on-call ownership, production authentication mapping, deployment, recovery authority and all real fallback routes.
