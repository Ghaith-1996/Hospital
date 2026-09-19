# Phase 10 implementation plan

Status: in progress, authorized September 19, 2026. Specification: [design](../specs/2026-09-19-phase-10-audit-observability-design.md). Baseline: 44f18535ca202333ea526046f50531331cc3d721. Branch: feature/phase-10-audit-observability.

Use the owner's full Phase 10 request as the acceptance checklist. Execute inline in focused commits. All paths below are repository-relative. List any additional exact paths in the slice record before editing. For every functional task write focused tests first, run expected RED, implement, run GREEN and the relevant regression project, update docs, inspect diff and commit. Documentation-only changes need no artificial tests. Never edit historical migrations. Never begin Phase 11.

## Task 1 — Design and governance
Files: this plan; docs/superpowers/specs/2026-09-19-phase-10-audit-observability-design.md; docs/architecture/observability.md; AGENTS.md; README.md; docs/product/workflow.md; docs/product/definition-of-done.md; docs/security/logging-policy.md; docs/security/production-readiness-gates.md.
Document verified baseline, all boundaries and production decisions. Run sensitive-data scan and git diff --check. Commit docs: define phase 10 observability boundary.

## Task 2 — Audit application/API
Files: src/backend/CriticalAlerts.Application/Audit/AuditContracts.cs; src/backend/CriticalAlerts.Application/Audit/AuditSafety.cs; src/backend/CriticalAlerts.Infrastructure/Audit/AuditQueryService.cs; src/backend/CriticalAlerts.Api/Http/AuditEndpoints.cs; src/backend/CriticalAlerts.Application/Identity/AuthorizationPolicies.cs; src/backend/CriticalAlerts.Api/Authentication/DevelopmentAuthenticationServiceCollectionExtensions.cs; src/backend/CriticalAlerts.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs; src/backend/CriticalAlerts.Api/Program.cs; tests/CriticalAlerts.Api.IntegrationTests/AuditQueryTests.cs; tests/CriticalAlerts.Application.Tests/AuditSafetyTests.cs.
RED roles, scope, filters, ordering, bounds, invalid cursor, projection and audit.read; GREEN application/API projects. Generate docs/api/openapi.json through scripts/verify-openapi.ps1. Commit feat: add safe audit query surface.

## Task 3 — PostgreSQL append-only/indexes
Files: src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/ReliabilityConfigurations.cs; src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/<generated>_Phase10AuditProtection.cs plus Designer/model snapshot; tests/CriticalAlerts.Infrastructure.Tests/AuditStorageTests.cs.
RED direct UPDATE/DELETE denial, INSERT success, fresh migrations and page traversal. Add only query-relevant indexes; GREEN infrastructure regression. Commit feat: harden append-only audit storage.

## Task 4 — Connected audit UI
Files: src/web/app/admin/audit/page.tsx; src/web/features/connected/audit-events.tsx; src/web/lib/audit.ts; src/web/components/layout/app-shell.tsx; src/web/tests/connected-audit.test.tsx.
RED loading/empty/data/filter/cursor/shape/401/403/retry/keyboard/metadata safety and role navigation. GREEN web tests/typecheck/lint/build/storage checks. Commit feat(web): add connected audit viewer.

## Task 5 — Logging, correlation and metrics
Files: src/backend/CriticalAlerts.Infrastructure/Observability/CriticalAlertsOperationalLog.cs; src/backend/CriticalAlerts.Infrastructure/Observability/PlatformMetrics.cs; src/backend/CriticalAlerts.Api/Program.cs; src/backend/CriticalAlerts.Api/Health/DatabaseHealthCheck.cs; src/backend/CriticalAlerts.Worker/Program.cs; src/backend/CriticalAlerts.Worker/SimulationDispatchWorker.cs; src/backend/CriticalAlerts.Worker/SimulationEscalationWorker.cs; existing Infrastructure Alerts/Directory/Dispatch/Responses/Escalation services at their commit boundaries; tests/CriticalAlerts.Api.IntegrationTests/ObservabilitySafetyTests.cs; tests/CriticalAlerts.Infrastructure.Tests/PlatformMetricsTests.cs.
RED runtime sentinel leaks including framework logs and query/correlation; metric values and strict tag vocabulary. Harden shared boundary and instrument committed operations. Preserve liveness/readiness independence from worker. GREEN affected projects; document emitted event coverage. Split logging and metrics commits.

## Task 6 — Operational guidance
Files: src/backend/CriticalAlerts.Application/Responses/OperationalWarnings.cs; src/backend/CriticalAlerts.Application/Responses/AlertLiveContracts.cs; src/backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs; src/web/lib/alerts.ts; src/web/features/connected/live-alert.tsx; tests/CriticalAlerts.Api.IntegrationTests/OperationalWarningTests.cs; src/web/tests/connected-responses.test.tsx.
RED provider failure, delayed dispatch/escalation, stale directory and exhaustion; preserve Phase 9 fields and polling cleanup. GREEN backend/web regression and OpenAPI. Commit feat: surface operational failure guidance.

## Task 7 — Restore exercise and runbooks
Files: scripts/db-restore-test.ps1; src/backend/CriticalAlerts.Infrastructure/Persistence/RestoreValidation.cs; src/backend/CriticalAlerts.Infrastructure/Persistence/DatabaseCommandHost.cs; src/backend/CriticalAlerts.Api/Program.cs; tests/CriticalAlerts.Infrastructure.Tests/RestoreValidationTests.cs; scripts/test-db-restore-safety.ps1; .gitignore; docs/runbooks/local-development.md; docs/runbooks/notification-provider-outage.md; docs/runbooks/directory-sync-failure.md; docs/runbooks/database-restore-test.md.
RED fail-closed guards and read-only schema/integrity validation; actual pg_dump/pg_restore success and injected-failure cleanup. GREEN infrastructure and restore exercise. Record measured durations without RPO/RTO claims. Focused restore/runbook commits.

## Task 8 — Observability and connected gate
Files: scripts/verify-observability-safety.ps1; scripts/test-all.ps1; .github/workflows/ci.yml; tests/e2e/audit-system.spec.ts; existing system harness helpers only as required.
Run focused runtime safety checks, add Scenario J to existing PostgreSQL/API/workers/Next harness including a Phase 9 event. Verify cleanup, no protected output/artifacts, complete OpenAPI responses. Commit test: verify runtime observability safety.

## Task 9 — Full verification and owner package
Files: docs/superpowers/phase10-verification.md; this plan and governance/observability docs as results become available.
Run every applicable owner gate, record commands/exact counts, migrations, containers, connected evidence, restore and failure cleanup, known limitations and production decisions. Inspect tracked artifacts and complete diff. Commit docs: complete phase 10 verification only after all gates pass. Propose a tag without creating one. Stop for owner review.

## Completed slice evidence

Task 2 API complete: AuditQueryTests initially failed 21/21 with missing-route 404; all 21 passed after endpoint/scoped service implementation. Full API regression 190/190 passed. OpenAPI generated and verified against runtime; sensitive-data scan and diff check passed. Contract/projection commit is `427bea8`. Audit service rejects unknown/repeated query keys and failure to append audit.read yields 503. No database mutation protection is claimed yet.

Docker recovered without reset by preserving only inspected zero-length runtime sockets in local directories `Docker/run.phase10-stale`, `Docker/run.phase10-retry2`, and `docker-secrets-engine.phase10-retry2`. These host recovery artifacts are outside the repository. Engine 29.1.3 responds. Recovered backend baseline plus contract tests passed 514/514 (Domain 92, Application 69, Architecture 9, Infrastructure 175, API 169). Earlier dependency failures remain recorded above and are superseded by this executed pass.

Task 3 complete: `20260919150045_Phase10AuditProtection.cs`, generated Designer/model snapshot, ReliabilityConfigurations.cs, AuditStorageTests.cs. RED 5/5: existing direct UPDATE/DELETE/TRUNCATE permitted and expected indexes absent. GREEN 5/5 and full Infrastructure 180/180. Restricted role INSERT succeeds while mutation fails; no application bypass. Historical migrations unchanged. Retained audit query cases also run against the new migration. Schema rollback is explicit migration authority, not runtime retention.

## Interface preflight
Tasks 2/3 share descending timestamp/UUID cursor order and indexes. Task 4 consumes only Task 2 projected fields/cursor. Task 5 restricts correlation used by Task 2 and runtime logs; use one opaque syntax. Task 6 adds warnings without replacing Phase 9 escalation. Task 7 includes every Phase 9 table and Phase 10 migration; no append-only bypass needed for empty-database restore. Task 8 uses the existing worker control/teardown harness. No conflicting interface was found; exact types will be checked in each slice.

## Execution record
Task 1 complete: design/governance commit `73c5eab`; sensitive-data scan and diff check pass.

Task 2 split: pure application contracts/projection can be verified independently while Docker startup is repaired. Exact files: `src/backend/CriticalAlerts.Application/Audit/AuditContracts.cs`, `AuditSafety.cs`, `tests/CriticalAlerts.Application.Tests/AuditSafetyTests.cs`. Initial RED: missing Audit module; focused GREEN 15/15. Review added eight retained Phase 9 action/outcome cases: RED 8/8, then GREEN. Full Application project 69/69 passes, zero failed/skipped. Targeted dotnet format, sensitive-data scan and diff check pass. Task 2 API/persistence/authorization remain pending; these unit results do not establish PostgreSQL query behavior. The projection uses finite strings, drops unsupported metadata including arbitrary policy/reason strings, and returns null for unsafe legacy correlation values. No existing audit producer or workflow is changed.

Baseline regression attempted against the unchanged compiled Phase 9 source: Domain 92/92, Application 46/46, Architecture 9/9; Infrastructure 14 passed/161 failed and API 33 passed/136 failed because the Docker engine could not start. No relational failure is attributed to code without a working dependency. Root cause: Windows-inaccessible runtime sockets; safe directory-preservation recovery is in progress. No volumes/databases were reset.
