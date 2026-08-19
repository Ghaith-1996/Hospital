# Phase 1 Scaffold and Local Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Create a fresh, reproducible local platform scaffold with empty modular-monolith projects, a PostgreSQL 18 development container, health endpoints, test projects, root scripts, and a CI baseline without implementing business behavior.

**Execution status (2026-08-19):** Tasks 1–5 are implemented within the reviewed Phase 1 boundary. Build, non-container tests, web tests, typecheck, lint, Next build, safety scan, compose validation, and dependency audits pass. Docker-dependent tests and the Playwright smoke test remain pending because Docker Desktop is unavailable and the local Playwright browser binary is not installed. Task 6 is the human review gate; no Git commit was created because this workspace is not initialized as a Git repository.

**Architecture:** Keep the API, worker, future connector, domain/application/infrastructure libraries, and Next.js web interface in one modular monolith. The API and worker share the planned application boundaries but do not dispatch, manage alerts, or connect to hospital systems in Phase 1. PostgreSQL is available locally for health and integration-test infrastructure; migrations and domain tables belong to Phase 2.

**Tech Stack:** C# 14, ASP.NET Core/.NET 10 LTS, EF Core 10, Npgsql 10, PostgreSQL 18, TypeScript, React, Next.js App Router, Node.js 24 LTS, xUnit, FluentAssertions, Testcontainers for .NET, Vitest, Testing Library, Playwright, Docker Compose, GitHub Actions, and PowerShell 7.

**Spec:** README.md, AGENTS.md, docs/product/definition-of-done.md, docs/architecture/containers.md, docs/architecture/data-model.md, and docs/security/production-readiness-gates.md.

## Global Constraints

- Use fictional hospitals, staff, practitioners, patients, phone numbers, and clinical data only.
- Use REQUIRES_HOSPITAL_DECISION for every missing real workflow, escalation, privacy, security, identity, directory, communications, retention, hosting, or integration decision.
- AI may assist with transcription and formatting only; it may not diagnose, assign urgency, select final doctors, stop escalation, or dispatch autonomously.
- No alert dispatch exists in Phase 1; later dispatch will require explicit human confirmation of the exact alert version and recipients.
- Preserve original typed/transcribed content separately from structured suggestions and approved content in the future domain model; Phase 1 creates no alert model.
- Require confirmation of every critical number and unit in the future workflow; Phase 1 creates no clinical field behavior.
- SMS and voicemail are not connected; future adapters default to generic wake-up content only.
- Delivered, opened, acknowledged, and responsibility accepted remain separate future states.
- Use a modular monolith with C#/.NET 10, PostgreSQL 18, EF Core 10/Npgsql 10, TypeScript, React, and Next.js.
- Pin exact approved SDK, runtime, package, and container-image versions; a mutable major-only tag such as `postgres:18` is not acceptable in committed configuration. If an exact version or image digest is not selected, record `REQUIRES_PROJECT_OWNER_DECISION` before implementation.
- Use TDD and real PostgreSQL integration tests; never substitute an in-memory database for relational tests.
- Never commit secrets or sensitive information. The .env file is local-only and ignored.
- Do not create migrations, domain entities, alert commands, business UI, provider adapters, hospital connectors, or production infrastructure in Phase 1.
- Stop after the Phase 1 review gate and report files, commands, tests, limitations, human actions, and a proposed commit message.

## Phase 1 file map

Create the following groups of files during Phase 1. Exact project names are fixed by the master plan; no other product code is authorized by this plan.

| Group | Files | Responsibility |
|---|---|---|
| Root toolchain | .editorconfig, .gitignore, global.json, Directory.Build.props, Directory.Build.targets, Directory.Packages.props, package.json, package-lock.json, .env.example | Pin tool behavior, package versions, safe local configuration, and repository-wide commands. |
| Backend graph | src/backend/CriticalAlerts.sln, src/backend/CriticalAlerts.Domain/CriticalAlerts.Domain.csproj, src/backend/CriticalAlerts.Application/CriticalAlerts.Application.csproj, src/backend/CriticalAlerts.Infrastructure/CriticalAlerts.Infrastructure.csproj, src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj, src/backend/CriticalAlerts.Worker/CriticalAlerts.Worker.csproj, src/backend/CriticalAlerts.Connector/CriticalAlerts.Connector.csproj | Empty modular-monolith project graph with dependency direction only. |
| Web shell | src/web/package.json, src/web/package-lock.json, src/web/next-env.d.ts, src/web/next.config.ts, src/web/tsconfig.json, src/web/eslint.config.mjs, src/web/app/layout.tsx, src/web/app/page.tsx, src/web/app/globals.css, src/web/vitest.config.ts, src/web/vitest.config.mjs, src/web/playwright.config.ts | Minimal Next.js shell with a visible simulation/platform status, no alert behavior. |
| Local platform | compose.yaml, scripts/dev-up.ps1, scripts/dev-down.ps1, scripts/db-migrate.ps1, scripts/db-reset-demo.ps1, scripts/test-all.ps1, scripts/verify-no-sensitive-data.ps1 | PostgreSQL 18 lifecycle, documented migration/reset guards, test orchestration, and safety checks. Phase 1 migration/reset scripts must report that migrations/seed data are not yet present. |
| API/worker platform | src/backend/CriticalAlerts.Api/Program.cs, src/backend/CriticalAlerts.Api/Health/DatabaseHealthCheck.cs, src/backend/CriticalAlerts.Api/appsettings.json, src/backend/CriticalAlerts.Api/appsettings.Development.json, src/backend/CriticalAlerts.Worker/Program.cs | Live/readiness endpoints, safe configuration loading, and a worker shell with no business handlers. |
| Tests | tests/CriticalAlerts.Domain.Tests/*, tests/CriticalAlerts.Application.Tests/*, tests/CriticalAlerts.Infrastructure.Tests/*, tests/CriticalAlerts.Api.IntegrationTests/*, tests/CriticalAlerts.Architecture.Tests/*, src/web/tests/*, tests/e2e/* | Test project shells, health/configuration tests, project-reference tests, and a smoke-test harness. |
| CI | .github/workflows/ci.yml | Reproducible restore/build/test/lint/typecheck/secret-scan baseline. |

The Phase 1 implementation may add the minimum files required by these shells, such as test fixtures and project metadata, but it must not add business behavior or a schema.

## Interfaces and dependency direction

The project graph must enforce:

~~~text
CriticalAlerts.Domain
        ↑
CriticalAlerts.Application
        ↑
CriticalAlerts.Infrastructure
        ↑
CriticalAlerts.Api / CriticalAlerts.Worker / CriticalAlerts.Connector
~~~

The web shell calls only the versioned API boundary after health infrastructure is available. Phase 1 does not expose alert endpoints. Future ports for notification, transcription, structuring, directory, scheduling, sensitive-data protection, and message transport belong to later phases and must not be invented in the scaffold.

### Task 1: Pin the toolchain and create the empty project graph

**Files:**

- Create: .editorconfig
- Create: .gitignore
- Create: global.json
- Create: Directory.Build.props
- Create: Directory.Build.targets
- Create: Directory.Packages.props
- Create: package.json
- Create: package-lock.json
- Create: src/backend/CriticalAlerts.sln
- Create: src/backend/CriticalAlerts.Domain/CriticalAlerts.Domain.csproj
- Create: src/backend/CriticalAlerts.Application/CriticalAlerts.Application.csproj
- Create: src/backend/CriticalAlerts.Infrastructure/CriticalAlerts.Infrastructure.csproj
- Create: src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj
- Create: src/backend/CriticalAlerts.Worker/CriticalAlerts.Worker.csproj
- Create: src/backend/CriticalAlerts.Connector/CriticalAlerts.Connector.csproj
- Create: tests/CriticalAlerts.Domain.Tests/CriticalAlerts.Domain.Tests.csproj
- Create: tests/CriticalAlerts.Application.Tests/CriticalAlerts.Application.Tests.csproj
- Create: tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj
- Create: tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj
- Create: tests/CriticalAlerts.Architecture.Tests/CriticalAlerts.Architecture.Tests.csproj
- Test: tests/CriticalAlerts.Architecture.Tests/ProjectGraphTests.cs

**Interfaces:**

- Consumes: Phase 0 dependency rules in docs/architecture/containers.md.
- Produces: A buildable solution graph with no domain or alert behavior and a test that rejects forbidden project references.

- [ ] **Step 1: Write the failing project-graph test**

Create ProjectGraphTests.cs with tests named DomainProjectHasNoRuntimeOrInfrastructureReferences, ApplicationProjectReferencesDomainOnly, InfrastructureDoesNotReferenceApiOrWorker, and WebIsNotAProjectReference. The tests should inspect project metadata from a deterministic project map and fail because the project files do not yet exist.

- [ ] **Step 2: Run the architecture test to verify it fails**

Run:

~~~powershell
dotnet test tests/CriticalAlerts.Architecture.Tests/CriticalAlerts.Architecture.Tests.csproj --no-restore
~~~

Expected: FAIL because the Phase 1 project graph and test project have not been created.

- [ ] **Step 3: Create the minimal pinned toolchain and project graph**

Use global.json to pin the installed .NET 10 SDK policy, central package management for test/runtime packages, and project references that follow the dependency direction. Keep the domain and application projects free of ASP.NET Core, EF Core, Azure, provider SDK, and UI dependencies. Add only the package references required to compile the empty platform and its tests.

Record the exact .NET SDK version, Node.js version, package versions, and PostgreSQL 18 image digest selected for this phase. The selected local baseline is .NET SDK `10.0.100`, Node.js `24.16.0`, npm `11.13.0`, Next.js `16.3.1`, React/React DOM `19.2.1`, Vitest `3.2.7`, Playwright `1.55.1`, central EF Core `10.0.4`, Npgsql `10.0.3`, Npgsql EF provider `10.0.3`, Testcontainers `4.13.0`, SSH.NET `2026.0.0`, and PostgreSQL `18.4` image digest `sha256:a02db8cac496f15b094798a38254f14d6e00741f709360e5e00bb6668ea31636`. Use an immutable PostgreSQL image reference in compose.yaml; do not commit a mutable major-only tag. If the project owner has not selected the exact image digest, stop at this step with `REQUIRES_PROJECT_OWNER_DECISION`.

The .gitignore must ignore .env, user secrets, build output, test results, local database volumes, generated Next.js output, and IDE files. It must not ignore documentation or test fixtures by broad patterns.

- [ ] **Step 4: Run the architecture test and solution build**

Run:

~~~powershell
dotnet test tests/CriticalAlerts.Architecture.Tests/CriticalAlerts.Architecture.Tests.csproj
dotnet build src/backend/CriticalAlerts.sln --configuration Release
~~~

Expected: PASS for project-reference rules and a successful Release build with no alert/domain behavior.

- [ ] **Step 5: Commit the independently reviewable scaffold**

Run:

~~~powershell
git add .editorconfig .gitignore global.json Directory.Build.props Directory.Packages.props package.json src/backend tests/CriticalAlerts.Architecture.Tests
git commit -m "build: add phase 1 modular monolith scaffold"
~~~

If the repository has not been initialized by the project owner, stop and report that human action instead of initializing or committing automatically.

### Task 2: Add the Next.js web shell without business behavior

**Files:**

- Create: src/web/package.json
- Create: src/web/package-lock.json
- Create: src/web/next-env.d.ts
- Create: src/web/next.config.ts
- Create: src/web/tsconfig.json
- Create: src/web/eslint.config.mjs
- Create: src/web/app/layout.tsx
- Create: src/web/app/page.tsx
- Create: src/web/app/globals.css
- Create: src/web/vitest.config.ts
- Create: src/web/vitest.config.mjs
- Create: src/web/playwright.config.ts
- Test: src/web/tests/page.test.tsx
- Test: tests/e2e/platform-smoke.spec.ts

**Interfaces:**

- Consumes: The API health endpoint contract from Task 3 only after that task is complete; the shell may display static platform status before then.
- Produces: A keyboard-accessible, no-business-behavior Next.js App Router page that visibly states SIMULATION MODE and does not present an alert dispatch control.

- [ ] **Step 1: Write the failing web tests**

Create tests named rendersSimulationModeBanner, doesNotRenderDispatchControl, and hasAVisiblePageTitle. The browser smoke test should navigate to / and assert the banner and title.

- [ ] **Step 2: Run the web tests to verify they fail**

Run:

~~~powershell
Push-Location src/web
npm test -- --run
Pop-Location
~~~

Expected: FAIL because the Next.js shell and test configuration do not yet exist.

- [ ] **Step 3: Create the minimal web shell**

Create a small App Router page with a text banner exactly containing SIMULATION MODE, a short statement that Phase 1 has no alert behavior, and keyboard-visible focus styles. Do not add routes for compose, recipients, review, live status, doctor inbox, or admin behavior in this phase.

Pin Node/Next/React/testing dependencies in the web package metadata and commit the generated package-lock.json so npm ci is reproducible. Do not use a remote image, analytics script, real hospital branding, real endpoint, or real data.

- [ ] **Step 4: Run unit, type, lint, and smoke tests**

Run:

~~~powershell
npm ci
Push-Location src/web
npm ci
npm test -- --run
npm run typecheck
npm run lint
Pop-Location
npm run web:e2e
~~~

Expected: PASS with only the platform shell and simulation banner.

- [ ] **Step 5: Commit the independently reviewable web shell**

Run:

~~~powershell
git add src/web tests/e2e/platform-smoke.spec.ts
git commit -m "build: add phase 1 web shell"
~~~

### Task 3: Add API live/readiness endpoints and an empty worker shell

**Files:**

- Create: src/backend/CriticalAlerts.Api/Program.cs
- Create: src/backend/CriticalAlerts.Api/Health/DatabaseHealthCheck.cs
- Create: src/backend/CriticalAlerts.Api/appsettings.json
- Create: src/backend/CriticalAlerts.Api/appsettings.Development.json
- Create: src/backend/CriticalAlerts.Worker/Program.cs
- Create: tests/CriticalAlerts.Api.IntegrationTests/HealthEndpointsTests.cs
- Create: tests/CriticalAlerts.Application.Tests/WorkerConfigurationTests.cs

**Interfaces:**

- Consumes: The API project graph from Task 1. A deterministic unavailable-database test double is used for this task; the real PostgreSQL readiness path is verified again after Task 4.
- Produces: GET /health/live for process liveness and GET /health/ready for dependency readiness, plus a worker that starts with no business handlers.

- [ ] **Step 1: Write failing health and configuration tests**

Create tests named LiveHealthDoesNotRequireDatabase, ReadyHealthReportsDatabaseFailureSafely, and WorkerDoesNotRegisterAlertDispatchHandlers. Assertions must inspect status codes and safe problem/status payloads only; they must not print connection strings or database contents. The healthy-readiness integration test belongs to Task 4 because it requires the real PostgreSQL fixture.

- [ ] **Step 2: Run the tests to verify they fail**

Run the platform tests with the database-unavailable test configuration:

~~~powershell
dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj --no-restore
dotnet test tests/CriticalAlerts.Application.Tests/CriticalAlerts.Application.Tests.csproj --no-restore
~~~

Expected: FAIL because the endpoints and worker configuration do not yet exist.

- [ ] **Step 3: Implement the minimal platform behavior**

Register liveness independently from PostgreSQL. Register readiness with a PostgreSQL connectivity check that reports a safe category and correlation ID without exposing the connection string, SQL, or data. Configure RFC 7807-compatible errors and correlation IDs for future endpoints, but do not create alert routes.

The worker must start, log only safe lifecycle metadata, and exit/fail clearly on unsafe configuration. It must not register outbox, notification, escalation, or provider handlers in Phase 1.

- [ ] **Step 4: Run targeted tests and manual endpoint checks**

Run:

~~~powershell
dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj
dotnet test tests/CriticalAlerts.Application.Tests/CriticalAlerts.Application.Tests.csproj
dotnet run --project src/backend/CriticalAlerts.Api/CriticalAlerts.Api.csproj --environment Development
~~~

While the API is running, verify GET /health/live returns success without a database and GET /health/ready reports a safe dependency result. Do not paste connection strings or response bodies containing sensitive values into logs.

- [ ] **Step 5: Commit the platform endpoints and worker shell**

Run:

~~~powershell
git add src/backend/CriticalAlerts.Api src/backend/CriticalAlerts.Worker tests/CriticalAlerts.Api.IntegrationTests tests/CriticalAlerts.Application.Tests
git commit -m "feat: add phase 1 health endpoints"
~~~

### Task 4: Add local PostgreSQL 18 and real Testcontainers infrastructure

**Files:**

- Create: compose.yaml
- Create: .env.example
- Create: scripts/dev-up.ps1
- Create: scripts/dev-down.ps1
- Create: scripts/db-migrate.ps1
- Create: scripts/db-reset-demo.ps1
- Create: tests/CriticalAlerts.Infrastructure.Tests/PostgresFixture.cs
- Create: tests/CriticalAlerts.Infrastructure.Tests/PostgresConnectivityTests.cs
- Modify: src/backend/CriticalAlerts.Api/Health/DatabaseHealthCheck.cs
- Modify: tests/CriticalAlerts.Api.IntegrationTests/HealthEndpointsTests.cs

**Interfaces:**

- Consumes: PostgreSQL 18 requirement and synthetic-data rules from docs/product/demo-data-rules.md.
- Produces: A local-only PostgreSQL service and a reusable Testcontainers fixture that proves tests use real PostgreSQL, not an in-memory provider.

- [ ] **Step 1: Write failing infrastructure tests**

Create tests named StartsPostgres18Container, CanOpenRealPostgresConnection, and DoesNotCreateSchemaInPhase1. The last test asserts that Phase 1 has no migration or schema initialization behavior.

- [ ] **Step 2: Run the infrastructure tests to verify they fail**

Run:

~~~powershell
dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj --no-restore
~~~

Expected: FAIL because the Testcontainers fixture and PostgreSQL configuration do not yet exist.

- [ ] **Step 3: Create the local container and guarded scripts**

Use an immutable, project-owner-approved PostgreSQL 18 image digest with a health check and an explicit local port variable. .env.example contains placeholders only; .env is ignored. dev-up.ps1 starts PostgreSQL and waits for health. dev-down.ps1 stops the service without deleting volumes. db-migrate.ps1 reports that migrations are not yet available and exits without changing the database. db-reset-demo.ps1 refuses to run outside Development/Test and refuses to delete anything in Phase 1 because no demo database reset implementation exists.

The scripts must never print passwords, full connection strings, or provider endpoints. They must use explicit paths and fail on invalid environment/configuration.

- [ ] **Step 4: Implement the Testcontainers fixture and run real integration tests**

Use the PostgreSQL 18 container image, wait for readiness, create a connection using the generated container connection string in memory, execute only a harmless connectivity query, and dispose the container after the test run. Do not add an EF Core in-memory provider.

Run:

~~~powershell
./scripts/dev-up.ps1
dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj
dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj --filter ReadyHealthReportsHealthyWhenDatabaseIsAvailable
docker compose ps
~~~

Expected: PASS for real PostgreSQL connectivity and an empty-schema assertion; the local container reports healthy.

- [ ] **Step 5: Commit the local database platform**

Run:

~~~powershell
git add compose.yaml .env.example scripts tests/CriticalAlerts.Infrastructure.Tests src/backend/CriticalAlerts.Api/Health/DatabaseHealthCheck.cs
git commit -m "build: add local postgres and testcontainers"
~~~

### Task 5: Add root scripts and CI verification

**Files:**

- Create: scripts/test-all.ps1
- Create: scripts/verify-no-sensitive-data.ps1
- Create: .github/workflows/ci.yml
- Modify: package.json
- Modify: README.md
- Test: tests/CriticalAlerts.Architecture.Tests/RepositorySafetyTests.cs

**Interfaces:**

- Consumes: Test commands and safe-data rules from AGENTS.md, docs/product/demo-data-rules.md, and docs/security/logging-policy.md.
- Produces: One documented local verification command and a CI job that runs the Phase 1 checks without secrets or production services.

- [ ] **Step 1: Write failing repository-safety tests**

Create RepositorySafetyTests.cs with tests named NoTrackedEnvFileExists, NoApplicationProjectContainsPhase2Migration, NoFixtureContainsNonSyntheticPhonePattern, and NoProviderCredentialPatternExists. The tests should fail until the repository checks and file set are present.

- [ ] **Step 2: Run the safety tests to verify they fail**

Run:

~~~powershell
dotnet test tests/CriticalAlerts.Architecture.Tests/CriticalAlerts.Architecture.Tests.csproj --no-restore
~~~

Expected: FAIL with missing repository-safety implementation or missing scaffold files, never with a leaked secret printed to output.

- [ ] **Step 3: Implement the root verification scripts and CI job**

test-all.ps1 runs backend build/tests, real PostgreSQL Testcontainers integration tests, web install/test/typecheck/lint, and Playwright smoke tests. It stops on failures and prints only command/status summaries. verify-no-sensitive-data.ps1 scans the workspace files and generated test output for secret patterns, real-looking endpoint patterns, forbidden fixture forms, and accidental .env files; it uses an allowlist for documented synthetic values.

The GitHub Actions job uses the exact runtime, package, and PostgreSQL image versions recorded in Task 1. Testcontainers starts disposable PostgreSQL only for tests that need it; the job runs secret scanning and never contains a credential. It must not deploy or contact real providers.

- [ ] **Step 4: Run the complete Phase 1 verification**

Run:

~~~powershell
./scripts/verify-no-sensitive-data.ps1
./scripts/test-all.ps1
~~~

Expected: PASS with no application business behavior, no migrations, no real provider calls, and no sensitive values in output.

- [ ] **Step 5: Commit the verification baseline**

Run:

~~~powershell
git add scripts package.json .github/workflows/ci.yml README.md tests/CriticalAlerts.Architecture.Tests/RepositorySafetyTests.cs
git commit -m "ci: add phase 1 verification baseline"
~~~

### Task 6: Fresh-clone review and Phase 1 gate

**Files:**

- Modify: README.md
- Modify: AGENTS.md
- Modify: docs/product/definition-of-done.md
- Modify: docs/superpowers/plans/2026-08-19-phase-1-scaffold-and-local-platform.md
- Test: CI artifacts and local command output

**Interfaces:**

- Consumes: All Phase 1 tasks and Phase 0 review approval.
- Produces: Evidence that a fresh clone runs locally with PostgreSQL and the platform shells, plus a stop for human review.

- [ ] **Step 1: Create a clean verification directory without changing the source workspace**

Use a temporary directory or a fresh clone created by the project owner. Do not copy .env, local volumes, credentials, or untracked sensitive files.

- [ ] **Step 2: Run the documented setup commands**

Run:

~~~powershell
Copy-Item .env.example .env
./scripts/dev-up.ps1
./scripts/test-all.ps1
~~~

Expected: the PostgreSQL container is healthy, the API and web shell build, health tests pass, the real PostgreSQL Testcontainers tests pass, and no alert/domain behavior exists.

- [ ] **Step 3: Verify Phase 1 boundaries**

Search the repository for alert commands, migrations, provider SDKs, real endpoints, production identity configuration, clinical payloads, and non-synthetic data. Any result must be removed or documented as a Phase 1 test/platform requirement; no business implementation may remain.

- [ ] **Step 4: Update the phase report and stop**

Record the exact files changed, architectural decisions, commands and results, known limitations, human actions, proposed commit message, and the fact that Phase 2 has not started. Request explicit human approval before adding database/domain behavior.

## Self-review checklist

- [ ] Every Phase 0 safety rule is preserved in the implementation plan.
- [ ] No task creates a migration, domain entity, alert endpoint, notification call, real identity integration, or production policy.
- [ ] Project references match the modular-monolith boundary.
- [ ] PostgreSQL integration uses Testcontainers with PostgreSQL 18.
- [ ] .env and secrets are ignored and never printed.
- [ ] Simulation-only behavior is visibly labelled.
- [ ] Each implementation task (Tasks 1–5) has an explicit failing test, expected failure, minimal implementation, passing test, and reviewable commit boundary; Task 6 is a review-only gate.
- [ ] No unresolved generic placeholder or unspecified implementation step is required to execute the plan.
