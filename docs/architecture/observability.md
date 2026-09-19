# Observability architecture

Status: Phase 10 design baseline; implementation and verification are tracked in the [plan](../superpowers/plans/2026-09-19-phase-10-audit-observability.md). The [design](../superpowers/specs/2026-09-19-phase-10-audit-observability-design.md) defines audit roles, pagination, projection, logs, metrics, health, worker semantics, runbooks and restore boundaries.

PostgreSQL remains authoritative for workflow and audit. The API provides scoped audit reads and minimal liveness/readiness. Worker dispatch and Phase 9 escalation retain exact human-confirmed snapshots, durable bounded recovery and independent delivery/responsibility state. Next.js reads these projections without browser persistence.

Phase 10 uses local ILogger and System.Diagnostics.Metrics only. No external monitoring, exporter, collector, hospital policy, retention approval or production readiness is configured.

## Proposed safe warning vocabulary
Each row defines application guidance, never clinical interpretation. Exact durable query conditions and test evidence will be recorded with implementation.

| Code | Condition | Safe message and next application action | Hospital fallback |
|---|---|---|---|
| ProviderUnavailable | Durable simulated provider-outage failure | Provider unavailable; alert remains recorded. Refresh durable delivery/outbox status. Do not create a duplicate. | Required when unresolved |
| DispatchDelayed | Pending/processing dispatch overdue by documented DEMO observation window | Notification processing delayed; confirmed alert remains durable. Refresh before retrying. | Required if persistent |
| DeliveryFailed | Durable delivery failure | Review failed attempt and bounded retry state; do not duplicate the alert. | Required when unresolved |
| DirectoryStale | Existing directory freshness evidence is stale | Review freshness before another recipient action. | No automatic fallback |
| DirectorySynchronizationFailed | Latest in-scope sync failure | Review safe sync status and valid source evidence before import. | Required if unresolved |
| DatabaseUnavailable | Dependency read cannot complete | Saved status cannot be refreshed. Restore API/database availability and reload before another action. | Required if unresolved |
| EscalationProcessingDelayed | Eligible scheduled/running evaluation overdue by DEMO window | Refresh confirmed DEMO escalation state; preserve exact recipients. | Required if persistent |
| EscalationExhausted | Durable exhausted run without completed responsibility/lifecycle | Automatic steps exhausted. Review delivery and responsibility separately. | Required |

All fallback routes are REQUIRES_HOSPITAL_DECISION. No contact, timing SLA, clinical severity or production threshold is invented. Failed reads retain the last known projection visibly marked stale; they do not fabricate current database facts.

## Implemented application projection

The application Audit module now defines a default 50/maximum 100 query, UTC inclusive lower/exclusive upper bounds, finite exact filters, a timestamp/UUID-only cursor, and explicit AuditEventView projection. Metadata is limited to bounded integer counts, simulationOnly boolean, finite channel/response values and filter-name arrays. Malformed, oversized, duplicate-key and unsupported nested data fail closed. Arbitrary policy/reason strings are omitted. Unknown top-level action/resource/outcome/actor strings become unknown; unsafe legacy correlation strings become null. Opaque actor/resource IDs remain available in the contract for the future authorized endpoint. Unit verification: 23 focused cases; API/storage integration is still pending.

## Audit API

GET `/api/v1/admin/audit` now requires AuditReader (Auditor or SystemAdministrator) and derives organization/actor from authenticated claims. It rejects unknown or repeated parameters; UTC filters use inclusive from/exclusive to; exact action/outcome/resource/correlation filters compose with PostgreSQL timestamp/UUID cursor comparison. Every page reads at most pageSize+1, returns at most 100 events, disables caching, then appends audit.read with pageSize, filter names and resultCount. Failed audit persistence yields safe 503 rather than an unaudited success. No recursion or raw query logging is added. Invalid query uses a constant RFC 7807 400 with the effective correlation ID. Runtime-generated OpenAPI declares 200/400/401/403/429/503. Verified: 21 focused PostgreSQL/API cases and 190 API regression tests. UI, database mutation protection and runtime log hardening remain separate pending slices.

## Append-only PostgreSQL storage

Migration `20260919150045_Phase10AuditProtection` installs a statement trigger rejecting UPDATE/DELETE/TRUNCATE with constant SQLSTATE 23514 and no row contents. INSERT remains permitted, including with a non-login restricted role granted table DML. There is no runtime escape setting. Administrative schema rollback explicitly drops the trigger/function; only schema authority can perform that operation, and production recovery authority remains REQUIRES_HOSPITAL_DECISION. Empty-database pg_restore can insert before restoring post-data triggers; no retention deletion is implemented.

The prior organization/time index is replaced by organization/time/ID. Additional organization/action/time/ID, organization/resource-type/time/ID and organization/correlation/time/ID indexes match implemented exact filters plus stable traversal. PostgreSQL can scan each B-tree backward for descending pages. No unimplemented resource-ID query index is added. Verified: five focused storage cases (RED then GREEN), full infrastructure 180/180; fresh test databases apply all migrations.

## Audit coverage source
The existing Phase 9 producers are AlertDraftService, AlertReviewService, DirectoryImportService, OutboxDispatchProcessor, RecipientResponseService, AlertLifecycleService, EscalationScheduler, EscalationRunProcessor and EscalationOverrideService. Implementation will document exact action names from those producers and add audit.read; this inventory does not claim any new event already exists.

## Production decisions
Audit retention/export/legal hold/review audience, central logs/log retention/SIEM, exporter/thresholds, incident severity/ownership, provider/directory fallback, database RPO/RTO/backup retention/recovery authority: REQUIRES_HOSPITAL_DECISION. Simulation exercise timings are measurements only.

The connected `/admin/audit` viewer uses transient component state and server cursors. Only Auditor/SystemAdministrator see its navigation link; server authorization remains authoritative. Fictional development handle `sim-auditor-avery` has only Auditor access. Response decoding independently rejects unexpected top-level vocabulary and projects metadata through the same finite technical types. Errors use fixed recovery text, with no server payload reflection.

Runtime logging enables only `CriticalAlerts.Operations` at Information or above; a post-configured filter suppresses framework request, SQL, and exception logs even when configuration requests verbose logging. The source-generated boundary emits request rejection, database readiness failure, worker state, and committed workflow operations. It accepts no request body, URL, exception, actor, organization, or metadata. Commit observers emit saved audit operations only after implicit or explicit PostgreSQL commit; rollback discards observations. These best-effort process logs are not the durable audit source. API correlation accepts only nonempty UUID N/D syntax, replaces other values, and attaches the effective ID before body-size checks. Health remains process-only/live and database-only/ready. Failed worker loops preserve durable lease recovery and use existing poll intervals.

Metric names: `criticalalerts.alert.confirmations`, `criticalalerts.outbox.processed`, `criticalalerts.outbox.failed`, `criticalalerts.dispatch.retries`, `criticalalerts.delivery.events`, `criticalalerts.responses`, `criticalalerts.directory.imports`, `criticalalerts.audit.queries`, `criticalalerts.escalation.steps`, `criticalalerts.escalation.stopped`, `criticalalerts.escalation.exhausted`, and `criticalalerts.escalation.failures`. Each counter increments by one per committed corresponding audit event, not per recipient or clinical result. The sole tag is `operation`, drawn from the closed mapping in PlatformMetrics. Unknown values emit no measurement. Counters are process-local, best effort, reset on restart, and cannot replace durable audit queries. No exporter is configured.
