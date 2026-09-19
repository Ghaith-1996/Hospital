# Phase 10 — Audit, Observability, and Runbooks

Status: implementation authorized by the owner's September 19, 2026 request. This is a design, not completion evidence. Stop after Phase 10 for project-owner review; no Phase 11.

## Verified baseline

Original checkout D:/hospital/Hospital: clean main at 9e312b2dc0e3a86e41bf1b97d2284020b193766a. Successful origin fetch and GitHub PR listing confirm main remains the Phase 8.5 merge (PR 4). No Phase 9 PR exists. Completed local Phase 9 is feat/phase-9-escalation at 44f18535ca202333ea526046f50531331cc3d721, clean before branching. Its specification, plan and phase9-verification.md are the source of truth. Phase 10 uses feature/phase-10-audit-observability from that exact commit in the existing linked worktree. Historical owner acceptance is not inferred; the current request explicitly authorizes Phase 10.

## Audit query and non-disclosure

GET /api/v1/admin/audit requires authentication and AuditReader (Auditor or SystemAdministrator only). Organization and actor come from server claims, never query values. No Operator, Practitioner, Physician, Administrator, DirectoryAdministrator or IntegrationAdministrator grant is inferred. Production mapping is REQUIRES_HOSPITAL_DECISION.

The application Audit module defines AuditQuery, AuditEventView, AuditPage and IAuditQueryService. Filters: UTC inclusive lower/exclusive upper occurrence, exact action/outcome/resource type, safe correlation ID. Default page size 50, maximum 100; invalid dates, filters, cursor or size return safe RFC 7807 400. No sort, organization, metadata query or export parameter. Ordering is OccurredAtUtc DESC, Id DESC using PostgreSQL UUID ordering; cursor encodes only the timestamp and UUID. Read pageSize+1 rows to determine next cursor. Independent requests may see newly inserted events; traversal excludes records ahead of the cursor.

Project technical fields explicitly. Metadata is parsed with depth/size limits and projected through typed key/value allowlists; unknown keys, arbitrary strings, nested objects and invalid types are omitted. Numeric counts are bounded, channel/response/reason values have finite vocabularies. Invalid legacy top-level technical strings are replaced with neutral categories; raw metadata never reaches the UI. Opaque actor ID is available only through AuditReader. Correlation accepts opaque UUID syntax only, preventing patient/contact/free-text IDs from becoming a diagnostic channel.

Each successful query writes audit.read after materializing the page, before returning it. Store only pageSize, filter names and resultCount. A failed audit append fails the request; no recursive query is used. New audit.read need not appear in its own page.

## Append-only storage and indexes

An additive migration rejects UPDATE and DELETE on audit_events in PostgreSQL, including direct SQL. INSERT and backup restore into an empty database remain functional. No application escape switch or retention deletion is added. Table owners can explicitly remove database objects for schema administration; this is not a runtime permission or approved production recovery procedure.

Replace the existing organization/time index with organization/time/ID for stable page traversal; add action and resource-type traversal indexes and correlation lookup only where query tests justify them. Do not add a resource-ID filter/index absent an actual query requirement. Historical migrations are immutable. Real PostgreSQL tests prove insertion, mutation rejection, ordering, cursor traversal and organization isolation.

## Provider-neutral logs and metrics

Use ILogger, strongly structured fixed templates, System.Diagnostics.Metrics and PostgreSQL audit. No exporter, external collector, vendor or network telemetry destination. Disable framework request/query/EF diagnostic categories that can echo URLs, bodies, exception text or SQL; retain explicit application events and minimal safe failure handling.

Allow only operation, channel, outcome, failure_category, response_type, attempt/retry/result counts, duration and safe correlation. Vocabulary is fixed at the boundary; omit arbitrary exception messages, exception objects, display names, provider references and raw payloads. Logger scopes use only the effective safe correlation ID. Metrics never tag organization/user/practitioner/alert/correlation/provider IDs or free text.

Meter: CriticalAlerts.Platform. Counters: criticalalerts.alert.confirmations, criticalalerts.dispatch.attempts, criticalalerts.dispatch.retries, criticalalerts.delivery.failures, criticalalerts.responses, criticalalerts.directory.imports, criticalalerts.outbox.processed, criticalalerts.outbox.failed, criticalalerts.audit.queries, criticalalerts.escalation.steps, criticalalerts.escalation.stopped, criticalalerts.escalation.exhausted. Histograms measure operation duration in milliseconds. Emit committed-success measurements after transaction commit; retries/replays must not claim another durable success. Metrics represent local process observations, not a durable accounting ledger.

## Health and worker semantics

/health/live checks only process liveness, independent of PostgreSQL. /health/ready checks PostgreSQL reachability; it does not claim that all workflow dependencies/schema are validated or that a worker is caught up. Keep current minimal status/check categories and effective correlation ID. No host, name, SQL, exception or credentials. Worker delay must never fail API readiness: API observation remains useful during asynchronous failure.

Workers preserve Phase 9 leases, shared locks, exact plans, bounded retry and recovery. A safe worker warning identifies the technical operation/failure; it cannot imply delivery, clinical resolution or a hospital fallback.

## Operational warnings

Extend the current live projection with an additive warnings collection, preserving every Phase 9 escalation field. Warning contract: code, title, explanation, recommendedAction, requiresHospitalFallback. Fixed vocabulary covers ProviderUnavailable, DispatchDelayed, DeliveryFailed, DirectoryStale, DirectorySynchronizationFailed, DatabaseUnavailable, EscalationProcessingDelayed and EscalationExhausted. Conditions and application actions are documented in observability.md against actual durable evidence. Delay thresholds used in fixtures are explicitly simulation assumptions, never hospital SLAs. Database outage can only be reported by safe failed-read recovery, not fabricated durable live state.

Warnings instruct refresh/review of durable state and approved manual fallback where relevant. Never create duplicates, unbounded retries, new recipients, provider switching, clinical urgency, real contacts or waiting periods. All hospital fallback routing remains REQUIRES_HOSPITAL_DECISION.

## Connected UI

/admin/audit uses the existing backend session and API proxy. Show time, action, resource type/ID, actor type, outcome, correlation and safe metadata. Bounded cursor navigation, labelled UTC filters, loading/empty/error/401/403/retry states and keyboard controls. No browser persistence or load-all/export. Navigation is role-aware; backend policy remains authoritative. Validate response shape before rendering; do not display raw API error content or unknown metadata.

## Backup/restore exercise

scripts/db-restore-test.ps1 -ConfirmRestoreTest fails closed without confirmation, explicit Development/Test, loopback source and approved simulation database name. Reject unknown/Staging/Production and unsafe targets. Use pinned PostgreSQL pg_dump/createdb/pg_restore/dropdb; never overwrite source. Create a unique empty temporary database and temporary dump; always clean both, including failure paths.

A read-only database validate-restore command checks migration history, model tables, organization-scoped relational invariants, safe counts (including Phase 9 tables), and application readability. Never print rows or pass connection strings as command arguments. Capture source invariants with an explicit quiescent fictional exercise; concurrent source writes cause mismatch/failure rather than a false pass. Report measured backup/restore/verification durations only. Production RPO/RTO are REQUIRES_HOSPITAL_DECISION.

## Runbooks and verification

Create exactly local-development.md, notification-provider-outage.md, directory-sync-failure.md, database-restore-test.md under docs/runbooks. Each contains Purpose, Scope, Detection, Safety impact, Immediate actions, What NOT to do, Diagnosis, Recovery, Verification, Evidence to preserve, Exit criteria, Production decisions. Commands must match the implementation.

Every functional slice starts with focused RED, then minimum implementation, targeted GREEN, relevant project regression, documentation/diff review and a focused commit. Runtime sentinels cover draft failure, confirmation, directory import, dispatch failure, response, lifecycle, audit and escalation success/failure. Capture formatted messages, structured properties, safe errors, metrics tags and health. Add MeterListener tests, role/scope/pagination/projection/append-only tests, connected UI tests and Scenario J in the existing system harness. Restore success and failure cleanup must execute against real fictional PostgreSQL.

Final gate runs pinned locked restore/format/build/backend/PostgreSQL/dependency checks, npm installs/tests/typecheck/lint/build, OpenAPI runtime match, all security/storage/observability scripts, actual restore exercise, standalone and connected Playwright, test-all, all three container builds and fresh migration/seed/readiness. Record exact executed results in phase10-verification.md; no unavailable check is a pass.

## Unresolved production decisions

Every item remains REQUIRES_HOSPITAL_DECISION: audit retention; audit export/legal hold; production audit reviewers; central log destination; log retention; SIEM integration; metric exporter; alert thresholds; incident severity; support/on-call ownership; provider outage fallback; directory outage fallback; database RPO; database RTO; backup retention; disaster recovery authority. No real provider, identity/directory/hospital connector, patient/employee data, AI or speech work.

