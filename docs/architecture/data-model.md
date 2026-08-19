# Conceptual Data Model

Status: Phase 0 conceptual model. It is a design boundary for fictional simulation data, not a production schema approval.

## Design rules

- Every organization-owned table carries `organization_id` or has an intentional, documented relationship to one.
- All timestamps are UTC.
- Alert and audit records are not hard-deleted through normal application code.
- Editable drafts use optimistic concurrency.
- Append-only state transitions, delivery events, responses, and audit events preserve chronology.
- Sensitive content is protected behind an `ISensitiveDataProtector` interface; no reversible key is placed in frontend code.
- Outbox payloads contain identifiers and control metadata only, not clinical bodies.
- Simulation rows use synthetic identifiers and are guarded by environment checks.

## Migration order from the master plan

### 001 — organizations and locations

Concepts: `organizations`, `sites`, and `departments`.

Production organization hierarchy, data residency, time zone, and tenancy semantics are `REQUIRES_HOSPITAL_DECISION`. Simulation uses one fictional organization and fictional locations.

### 002 — identity and authorization

Concepts: `users`, `roles`, `user_roles`, and `external_identities`.

Simulation identities are fixed fictional users. Production identity provider, tenant restriction, MFA, role mapping, break-glass process, and deprovisioning are `REQUIRES_HOSPITAL_DECISION`.

### 003 — practitioner directory

Concepts: `practitioners`, `practitioner_roles`, `contact_endpoints`, `on_call_assignments`, `directory_source_records`, and `directory_sync_runs`.

Never match a practitioner solely by name. Store stable source identifiers separately from the internal practitioner. Endpoint values are protected or represented by provider references. Production source, freshness threshold, deactivation, on-call meaning, and conflict resolution are `REQUIRES_HOSPITAL_DECISION`.

### 004 — templates and policies

Concepts: `alert_templates`, `notification_policies`, `escalation_policies`, and `escalation_steps`.

Templates, approved terminology, required fields, numeric confirmation fields, channel wording, retry limits, trigger conditions, stop conditions, delay, backup hierarchy, and override authority are `REQUIRES_HOSPITAL_DECISION`. Simulation policies are versioned and labelled `DEMO`.

### 005 — alerts and provenance

Concepts: `alerts`, `alert_field_confirmations`, `alert_recipient_selections`, and `alert_state_transitions`.

An alert stores or references the following separate representations:

| Representation | Purpose | Authority |
|---|---|---|
| Original typed source | Exact operator input. | Human-created source; immutable history. |
| Exact transcription | Exact provider transcript when dictation is enabled. | Provider output; not approved content. |
| Structured suggestion | Fielded/SBAR-style proposal, evidence spans, confidence, missing fields, and ambiguities. | Non-authoritative suggestion. |
| Approved message/version | Exact human-reviewed content for a specific draft version. | Dispatchable only after explicit confirmation. |
| Critical-field confirmation | Per-field approved value, unit, actor, time, and draft version. | Human confirmation required. |
| Recipient selection | Practitioner, selected channel, selection source, actor, and directory timestamp shown. | Manual human selection. |

The alert may carry a synthetic patient reference, location, operator-selected urgency, source type, protected source/approved content, structured payload reference, current draft version, workflow state, confirmation metadata, resolution metadata, and concurrency token. Full patient charts, real identifiers, and unapproved clinical payloads are out of scope.

### Source and draft version rule

Every source edit creates a new draft version. The typed source and exact transcription associated with each version remain immutable history; structured suggestions may be regenerated against a version; approved content references one exact version and cannot be silently replaced. A confirmation for an older version is rejected.

### 006 — deliveries and responses

Concepts: `delivery_attempts`, `delivery_events`, `recipient_responses`, `responsibility_assignments`, and `escalation_runs`.

Delivery, provider submission, delivery, opening, acknowledgement, responsibility acceptance, decline, unavailable, and escalation are separate records or state dimensions. Each channel declares whether a state is supported; unsupported states are recorded as `NotApplicable`, while supported but unseen states remain pending/not observed. Provider event IDs are unique and callbacks are idempotent.

The exact relationship between a response and a hospital responsibility transfer is `REQUIRES_HOSPITAL_DECISION`; the simulation keeps the events separate so no implied clinical responsibility is created.

### 007 — reliable work and audit

Concepts: `outbox_messages`, `inbox_messages`, `idempotency_records`, and `audit_events`.

Outbox payloads contain identifiers only. Inbox uniqueness is `(external_message_id, handler)`. Idempotency keys are scoped by organization, operation, and request hash. Audit events are append-only, actor/resource/action oriented, and sanitized.

## Relationships

```mermaid
erDiagram
    ORGANIZATION ||--o{ USER : owns
    ORGANIZATION ||--o{ PRACTITIONER : contains
    ORGANIZATION ||--o{ ALERT : scopes
    ALERT ||--o{ ALERT_FIELD_CONFIRMATION : has
    ALERT ||--o{ ALERT_RECIPIENT_SELECTION : targets
    ALERT ||--o{ ALERT_STATE_TRANSITION : records
    ALERT_RECIPIENT_SELECTION ||--o{ DELIVERY_ATTEMPT : creates
    DELIVERY_ATTEMPT ||--o{ DELIVERY_EVENT : receives
    ALERT_RECIPIENT_SELECTION ||--o{ RECIPIENT_RESPONSE : records
    ALERT ||--o{ RESPONSIBILITY_ASSIGNMENT : records
    ALERT ||--o{ ESCALATION_RUN : evaluates
    ALERT ||--o{ OUTBOX_MESSAGE : emits
    ORGANIZATION ||--o{ AUDIT_EVENT : owns
```

## Protection and retention

Formal classification, retention, deletion, legal hold, export, access review, encryption algorithms, key custody, and residency are `REQUIRES_HOSPITAL_DECISION`. Until approved, keep the simulation synthetic, minimize payloads, exclude clinical bodies from logs, and do not enable retention jobs.

## Database safety requirements for implementation

- Intentional foreign-key delete behavior for every relationship.
- Explicit indexes for active directory search, alert timelines, due escalation, and outbox leasing.
- Constraints for status values with a documented migration strategy.
- Separate migration and runtime database roles in production.
- Real PostgreSQL integration tests with Testcontainers.
- No in-memory substitute for relational behavior.
