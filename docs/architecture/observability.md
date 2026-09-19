# Observability architecture

Phase 10 extends completed Phase 9 commit `44f18535ca202333ea526046f50531331cc3d721`. PostgreSQL remains authoritative for workflow and audit. Next.js reads scoped projections without browser persistence. Dispatch and escalation retain human-confirmed snapshots, bounded recovery and independent delivery/responsibility state. See the [design](../superpowers/specs/2026-09-19-phase-10-audit-observability-design.md), [implementation record](../superpowers/plans/2026-09-19-phase-10-audit-observability.md), and [verification package](../superpowers/phase10-verification.md).

Only local ILogger, System.Diagnostics.Metrics, ASP.NET Core health checks and PostgreSQL audit storage are used. No external exporter, collector or production monitoring is configured.

## Audit access and projection

GET `/api/v1/admin/audit` requires `AuditReader`: Auditor or SystemAdministrator. Organization and actor come from authenticated server claims. Other roles are denied; frontend navigation is only a convenience. The fictional `sim-auditor-avery` identity has Auditor only and lands on `/admin/audit`.

Queries accept UTC inclusive from/exclusive to, exact finite action/outcome/resource-type, opaque correlation ID, cursor and page size. Unknown or repeated parameters fail with safe RFC 7807. Default page size is 50, maximum 100. Ordering is occurred-at descending then UUID descending. The cursor encodes only timestamp ticks and audit UUID; PostgreSQL performs tuple comparison. Each request reads at most pageSize+1 and returns at most pageSize events. There is no caller organization, SQL, sort or arbitrary metadata query.

AuditEventView exposes opaque event/resource/actor identifiers, fixed action/resource/outcome/actor vocabularies, safe correlation and UTC time. The projector never trusts SanitizedMetadata by name. Metadata has an 8192-character bound, bounded JSON depth, duplicate-key rejection, bounded integer counts, simulationOnly boolean, finite channel/response strings and filter-name arrays. Unexpected keys, nested objects, arbitrary reason/policy text and malformed values are omitted. Unknown top-level vocabulary becomes unknown; unsafe legacy correlation becomes null. The browser independently validates the contract and displays fixed errors.

Successful reads append `audit.read` containing only pageSize, filter names and resultCount, after reading the requested page. Filter values are excluded. Failure to persist access returns safe 503. Reads disable caching and do not recursively query their own access event. OpenAPI declares 200/400/401/403/429/503. Responses and problems carry the effective correlation header.

The existing API rate limiter retains its per-user/organization budget. A rejected request returns fixed RFC 7807 429 with the effective correlation and Retry-After derived from the limiter lease. The connected test harness respects that header with bounded retries only for explicit 429 responses; it does not disable or enlarge the application budget.

## Append-only storage

Additive migration `20260919150045_Phase10AuditProtection` installs a statement trigger rejecting UPDATE, DELETE and TRUNCATE using constant SQLSTATE 23514. INSERT remains functional, including under a restricted non-login role with table DML grants. There is no runtime bypass. The migration Down explicitly drops the trigger/function; schema-owner authority is outside ordinary application DML. Production recovery authority remains REQUIRES_HOSPITAL_DECISION. No retention deletion is implemented.

The former organization/time index becomes organization/time/ID. Organization/action/time/ID, organization/resource-type/time/ID and organization/correlation/time/ID indexes match implemented filters and stable traversal. PostgreSQL scans these B-trees backward. No unused resource-ID filter/index is introduced. Historical migrations are unchanged.

## Implemented audit coverage

These are exact persisted action names, not a proposed future inventory. Existing producers remain authoritative; Phase 10 adds audit.read.

| Workflow | Persisted actions |
| --- | --- |
| Draft creation/edit | `alert.draft.created`, `alert.draft.updated` |
| Critical field confirmation | `alert.critical-field.confirmed` |
| Submission for review | `alert.draft.submitted` |
| Approved message and recipient review | `alert.approved-message.updated`, `alert.recipients.replaced` |
| Human dispatch confirmation | `alert.confirmed` |
| Dispatch and bounded recovery | `dispatch.completed`, `dispatch.failed`, `dispatch.retry-scheduled`, `dispatch.suppressed` |
| Normalized delivery events | `dispatch.delivery-event` |
| Secure message opened | `recipient.opened` |
| Recipient responses | `recipient.response.acknowledged`, `recipient.response.accepted`, `recipient.response.declined`, `recipient.response.unavailable`, `recipient.response.callunitrequested` |
| Operator lifecycle | `alert.resolved`, `alert.cancelled` |
| Valid directory import | `directory.import.applied` |
| Audit access | `audit.read` |
| Phase 9 scheduling/activation/queue | `escalation-scheduled`, `escalation-recipients-activated`, `escalation-dispatch-queued` |
| Phase 9 terminal/failure | `escalation-stopped`, `escalation-exhausted`, `escalation-processing-failed` |
| Phase 9 human overrides | `escalation.paused`, `escalation.resumed` |

An invalid preview or rejected workflow request does not fabricate a successful business audit event. Request rejection is safely observable through the logging boundary. Audit storage is durable; logs and metrics are not substitutes.

## Structured logs and correlation

Only category `CriticalAlerts.Operations` at Information or higher is enabled. Post-configured filters suppress framework request, SQL and exception logs even under verbose configuration. Source-generated fixed events cover request rejection, database readiness failure, worker state and committed workflow operations. Fields are status code, finite operation/state and effective opaque correlation ID where applicable. No display name, organization/actor/resource ID, URL, query string, exception, metadata or business payload is accepted.

SaveChanges/transaction observers emit workflow observations only after successful implicit or explicit PostgreSQL commit; rollback discards pending observations. Worker dependency failures emit fixed retry-pending state and retain existing polling and durable lease recovery. These process observations are best effort and are not transactionally durable telemetry.

`X-Correlation-ID` accepts a UUID in N or D syntax; missing, malformed, oversized or other text is replaced with a server UUID. The effective value is established before body-size checks, returned in the response and used by request audit/log producers. Legacy worker correlations outside that syntax are omitted from the public audit projection and logged as unavailable rather than reflected.

## Metrics

Meter `CriticalAlerts.Platform` version 1.0.0 has counters `criticalalerts.alert.confirmations`, `criticalalerts.outbox.processed`, `criticalalerts.dispatch.failures`, `criticalalerts.dispatch.retries`, `criticalalerts.delivery.events`, `criticalalerts.responses`, `criticalalerts.directory.imports`, `criticalalerts.audit.queries`, `criticalalerts.escalation.steps`, `criticalalerts.escalation.stopped`, `criticalalerts.escalation.exhausted`, and `criticalalerts.escalation.failures`.

Each increments by one per corresponding committed audit operation, not per recipient, send attempt or clinical outcome. In particular, dispatch.failures counts both recipient and aggregate dispatch.failed records, not distinct failed outboxes. The sole tag is `operation`, from the closed mapping in PlatformMetrics. Unknown operations emit nothing. No identifiers, correlation, patient data, provider reference or free text are dimensions. Counters reset with the process; no exporter or production threshold is configured. MeterListener tests assert values, vocabulary and non-disclosure.

## Health and worker semantics

`/health/live` means the API process responds; it has no PostgreSQL dependency. `/health/ready` checks PostgreSQL and returns 503 when unavailable. Minimal responses contain safe status/category only, plus the effective correlation header. They contain no host, database name, SQL, credentials, connection string, exception or stack trace.

Worker delay does not fail API readiness: operators still need access to durable state. The worker preserves Phase 9 leases, confirmed plans and bounded retry behavior. Live operational warnings expose delay/failure without changing dispatch/escalation state or inventing responsibility.

## Operational warning vocabulary

Every warning includes code, fixed title/explanation, recommendedApplicationAction and requiresHospitalFallback. The browser maps only recognized codes to fixed local messages. No severity rank or contact route exists.

| Code | Durable condition | Safe message and next action | Fallback flag |
| --- | --- | --- | --- |
| ProviderUnavailable | Failed attempt has provider-unavailable | Simulated provider unavailable; alert remains recorded. Refresh; do not duplicate alert. | true |
| DeliveryFailed | Failed attempt or failed original outbox | Recorded attempt failed; delivery does not establish responsibility. Review attempts and refresh. | true |
| DispatchDelayed | Pending next-attempt or processing lease overdue by 30-second DEMO grace | Processing delayed; confirmation remains durable. Refresh before retrying. | true |
| DirectoryStale | Selected practitioner's scoped source record is stale | Selected information is marked stale. Review freshness/source before another recipient action. | false |
| DirectorySynchronizationFailed | Latest scoped sync is Failed or Partial | Latest synchronization did not succeed. Inspect safe status and validate a new import. | true |
| DatabaseUnavailable | Dependency unavailable; vocabulary for readiness/recovery because live query itself needs DB | Dependency operations unavailable. Check readiness and refresh before retrying. | true |
| EscalationProcessingDelayed | Scheduled/running evaluation overdue, processing failure or failed escalation outbox | Confirmed step overdue or failed. Review confirmed plan and refresh; never edit workflow state. | true |
| EscalationExhausted | Exhausted run without active responsibility | Approved automatic steps queued; delivery/responsibility remain separate. Review both and use approved fallback if unassigned. | true |

The 30-second grace is a simulation technical observation tolerance, not a clinical deadline, SLA, retry rule or production threshold. Paused/stopped runs do not warn merely because time passes. Existing Phase 9 live fields remain intact. All real fallback routes and thresholds remain REQUIRES_HOSPITAL_DECISION.

GET `/api/v1/directory/sync-status` uses existing DirectoryReader authorization and server organization. It exposes only latest fixed source/status, UTC times and counts; never ErrorSummary. Rejected previews/imports do not create a sync run. Stale and inactive remain separate source facts.

## Runbooks and restore verification

The four [runbooks](../runbooks/local-development.md) cover local development, notification outage, directory sync failure and database restore. Each contains Purpose, Scope, Detection, Safety impact, Immediate actions, What NOT to do, Diagnosis, Recovery, Verification, Evidence to preserve, Exit criteria and Production decisions.

`db-restore-test.ps1 -ConfirmRestoreTest` requires explicit Development/Test and a local Docker endpoint. It owns a fresh pinned PostgreSQL container and fictional source, uses real pg_dump/createdb/pg_restore/dropdb, and never accepts an existing source/restore target. A unique empty temporary database is restored; the application read-only validate-restore command verifies migrations, every EF table, foreign-key/org integrity, trigger availability and safe counts. Restored append-only behavior and unchanged source counts are checked. Cleanup drops the temporary DB, deletes the dump and removes the owned container/volume on success and injected failure. Workflow tables in this seed-only exercise may be empty; populated workflows are tested by the connected harness. Durations are simulation measurements, not RPO/RTO.

Runtime sentinel tests exercise draft rejection, confirmation, directory import, dispatch failure, responses, lifecycle, audit, escalation processing/failure, metrics and health. The focused safety script runs those checks and rejects tracked operational artifacts. Connected browser traces/video are disabled; only the allowlisted audit screen can be captured after protected-value checks.

## Open production decisions

Each remains REQUIRES_HOSPITAL_DECISION: audit retention; audit export/legal hold; production audit reviewers and role mapping; central log destination; log retention; SIEM integration; production metric exporter; alert thresholds; incident severity model; support/on-call ownership; provider outage fallback route; directory outage fallback; database RPO; database RTO; production backup retention; disaster recovery authority. No simulation verification approves production use.
