# Phase 9 — Exact Confirmed DEMO Escalation Design

Status: Phase 9 implementation authorized by the project owner's 2026-09-07 request. This document is the specification, not completion evidence or hospital approval. The Phase 8.5 verification and approval records remain historical evidence; its prohibition on starting Phase 9 is superseded by this explicit request only for Phase 9. Stop after Phase 9 for human review; do not start Phase 10.

## Goal and boundary

Implement a deterministic, durable escalation simulation in the existing modular monolith and worker. PostgreSQL owns saved state and UTC deadlines. Only Development/Test may enable `SimulationEscalation`; enabled configuration fails startup in Staging/Production. No real data, communication provider, external callback, hospital integration, production identity, AI, clinical reasoning, arbitrary replacement clinician, or production escalation period is introduced. Repository visibility/settings remain unchanged and public. All production policy is `REQUIRES_HOSPITAL_DECISION`.

Preserve the connected Phase 8.5 frontend and all Phase 0–8 safety tests. Browser memory holds only transient presentation and unsaved input. Browser timers and five-second live polling never execute escalation. The worker evaluates explicit, human-confirmed DEMO rules; it does not make a clinical decision.

## Exact human confirmation

The current `DemoEscalationPolicyVersion` placeholder is insufficient authorization. Before automation, review must expose the exact organization-scoped policy ID/version, deterministic plan revision, ordered steps, clearly labelled DEMO delays, retry bounds, stop/trigger semantics, and every future backup practitioner, role, channel, directory revision/source UTC timestamp and displayed on-call evidence. Preserve normal primary recipients and critical-field/message review.

Persist a plan for the exact organization/alert/version. Capture policy rules and steps as immutable confirmation snapshots, so editing even the same policy version later cannot alter approved execution. Policy editing is outside this phase. The DEMO resolver uses a fixed fictional backup definition and explicit deterministic directory evidence before review; it never ranks clinicians or uses clinical content. Duplicate primary/backup or cross-step practitioner/channel pairs must be rejected as an invalid plan before confirmation, with safe review guidance, rather than silently redispatched.

`Confirm` requires `ExpectedVersion`, `ExpectedEscalationPlanRevision`, and `Idempotency-Key`. Include the revision in the canonical idempotency request hash. Inside the confirmation transaction, re-resolve and compare policy, steps, directory and on-call evidence consistently. A changed review returns HTTP 409 `escalation-plan-changed`, asks for reload, and creates no confirmation, snapshot, audit success or dispatch. Serialize relevant directory/policy changes or use equivalent transaction isolation/revalidation so evidence cannot change between successful validation and commit. Confirmation, exact future snapshots, actor/time evidence, state transition, audit, idempotency result and initial identifier-only outbox commit atomically. Replays return the original result even if current directory data subsequently changes.

Each future recipient snapshot includes OrganizationId, AlertId, AlertVersion, EscalationPolicyId, EscalationPolicyVersion, PlanRevision, StepSequence, PractitionerId, PractitionerRoleId, Channel, DirectoryRevision, DirectorySourceUpdatedAtUtc, OnCallSnapshot, ConfirmedByUserId and ConfirmedAtUtc. Snapshot safe display evidence is separate from identifier-only operational events. Future snapshots are not active recipient selections, deliveries or inbox access until their step activates.

Legacy confirmed alerts lacking exact snapshots remain `automaticEscalationEligible = false`. Do not fabricate/backfill a policy or start a run from the old placeholder. Nullable additive columns preserve these historical records. A worker must never choose the current active policy after confirmation.

## DEMO semantics

| Durable fact | Simulation behavior |
|---|---|
| Delivered, opened, acknowledged or call-unit request only | Continue; none creates responsibility. |
| Active exact-version ResponsibilityAssignment | Stop; do not automatically resolve the alert. |
| Cancelled or Resolved alert | Stop. |
| Declined or Unavailable terminal response | Make the next eligible step immediately due, once per response signal. |
| Paused run | Do not activate any step; retain remaining delay durably. |
| Resume | Restore stored remaining delay from PostgreSQL UTC now; zero remaining means immediately due. |
| All automatic steps consumed without responsibility | Completed with Exhausted outcome and visible manual-fallback warning. |
| Invalid/inactive confirmed backup or processing failure | Durable visible failure and manual fallback; no substitute clinician. |

Stop precedence under the shared lock is lifecycle terminal state, active exact-version responsibility, pause, unconsumed decline/unavailable signal, deadline, then wait. Unit tests may supply deterministic UTC instants to the pure domain. Production runtime due/lease/pause decisions always use PostgreSQL time, never a fake clock, browser time or application wall clock.

## Durable execution and concurrency

Extend `EscalationRun` with exact AlertVersion, state, current step, next due UTC time, lease owner/expiry, paused time/remaining delay, safe stop reason, updated/completed UTC timestamps. Use explicit methods for Schedule, BeginProcessing, Advance, Pause, Resume, Stop, Complete and ReleaseLease; infrastructure may not bypass invariants with direct property mutation. Scheduling requires an exact policy/plan and confirmed version. Terminal runs cannot advance, paused runs cannot execute, a step cannot advance twice, timestamps must be UTC, and remaining delay cannot be negative.

An organization-scoped unique `(organization_id, alert_id, alert_version)` constraint is the final one-run guard. The scheduler finds Active confirmed eligible alerts without that exact run, uses database time for first due time, and persists Scheduled atomically. Subsequent due times derive from the persisted processing instant and the next confirmed DEMO delay; restart never recreates a fresh countdown. No run is created for Draft, PendingConfirmation, DispatchQueued, Failed or legacy alerts.

All acceptance/response, resolve/cancel, pause/resume and escalation scheduling/activation transactions acquire the same organization-scoped **alert row lock first**, then the escalation run row if needed, then dependent rows in stable order. Never hold a run lock while waiting for the alert lock. Candidate discovery is read-only; claim locks the alert before the run, with SKIP LOCKED/bounded batches where appropriate. Lease recovery follows the same order. After acquiring locks, reload exact-version lifecycle, responsibility, pause, lease and trigger state. Acceptance committed before activation acquires the alert lock is observed and prevents activation. If activation wins the lock first, its commit precedes acceptance; tests must distinguish this valid ordering from a stale read. Timestamp comparisons alone do not resolve the race.

Only the current unexpired lease owner may persist processing results. Lease recovery uses PostgreSQL time and a stable per-worker owner. Activation, consumed signal, step advance/outcome, timeline, audit and outbox are one transaction; rollback leaves none of them committed. Unique activation and logical-outbox keys make replay harmless after crash or lease expiry.

### Decline/unavailable consumption

Persist an explicit organization/run/response-ID consumption record, with a unique key and the step it triggered. Responses must belong to an already activated recipient and the exact organization/alert/version. One durable response can accelerate at most one eligible step across polls, restarts and workers. Do not query “any decline exists” as a permanent boolean. Multiple eligible signals are consumed in deterministic occurred-at/ID order, one per step. Pause does not consume signals; they remain pending until resume, after lifecycle/responsibility checks. Terminal/exhausted runs never reuse a signal to recreate work. Consumption and step effects commit together so rollback does not lose the signal.

### Backup activation and failure

Activate only exact preconfirmed practitioner/role/channel snapshots; recheck organization membership, active practitioner/role and usable channel before activation. A current safety check may reject an approved recipient but cannot expand or replace it. A now-inactive, missing, foreign, or channel-ineligible backup produces a sanitized durable ProcessingFailed outcome and manual fallback, with zero selection/outbox for the invalid step. Treat step activation atomically; do not silently dispatch a partial step. Do not search the directory for another clinician or mutate the confirmed plan. Later dispatch validation failure remains visible through the existing delivery path.

Successful activation creates exact-version `AlertRecipientSelection` rows with `SelectionSource = EscalationPolicy` and run/step provenance, without incrementing DraftVersion or altering approved content. It appends StepDue, RecipientActivated and DispatchQueued event types as applicable, plus audit and one `EscalationDispatchRequested` outbox item for the step, then advances/completes the run. Event cardinality is one of each logical type per step/recipient as applicable; “exactly one event” in concurrency tests refers to one logical activation event, not deletion of the other timeline facts.

Outbox payload allowlist: `alertId`, `alertVersion`, `escalationRunId`, `stepSequence`, `recipientSelectionIds`. No patient reference, message, practitioner name, phone, endpoint, ciphertext, raw provider payload or free text. Scope comes from durable outbox organization. Extend the Phase 7 processor to validate run/step/version and dispatch only the specified newly activated selections. Never redispatch original manual recipients; never call channel adapters directly from escalation. Preserve existing stable delivery-attempt keys, bounded retries and failures.

## Persistence and timeline

Add a new EF migration; never edit historical migrations. Extend alert exact-policy references and run fields. Persist immutable confirmed plan/steps/recipient snapshots, consumed trigger references, and append-only `escalation_events`. Use composite organization-scoped foreign keys and unique constraints for runs, activation, signal consumption and logical outbox work. Retain the due-query index and add lease/lookup indexes justified by actual queries. Enforce append-only events at the same persistence boundary as retained append-only records; test attempted update/delete rejection.

Events include Scheduled, StepDue, RecipientActivated, DispatchQueued, Paused, Resumed, StoppedByResponsibility, StoppedByResolution, StoppedByCancellation, Exhausted and ProcessingFailed. Fields are opaque organization/run/alert/version IDs, type, step, optional actor, allowlisted reason, correlation ID, sanitized identifier-only metadata and PostgreSQL UTC occurrence time. No clinical content or contact details enter events, audit, logs, errors, metrics or idempotency result storage.

## Human controls and projection

Only POST `/api/v1/alerts/{id}/escalation/pause` and `/resume` are added. Reuse the server-side lifecycle-operator simulation role policy. Authentication, server organization, exact confirmed version, mandatory idempotency key and allowlisted reason code are required. No free-text reason or third permanent stop action. Resolve/Cancel remain the explicit lifecycle stop paths. Pause/Resume persist audit and timeline atomically and replay without duplicate events; same key with another operation or payload conflicts. Errors include safe RFC 7807 400/401/403, non-disclosing cross-org 404 and 409 stale/invalid-state/key conflicts, with concrete OpenAPI responses.

Live projects eligibility, policy ID/version, run state, current step, next UTC evaluation, paused/stopped/exhausted flags, safe reason, manual fallback and append-only timeline. Each recipient identifies its selection source and confirmed DEMO policy/step. Review and live clearly label DEMO. Preserve last durable projection on API failure, surface staleness, clean up polling and synchronously block double clicks. An optional countdown is display only; reaching zero sends no command. Live excludes patientReference, approvedMessage, phone, providerReference and ciphertext. Safe fictional practitioner display data remains allowed where the existing live contract permits it.

## Verification and completion

Follow the accompanying task plan with failing tests, minimal implementation, targeted green tests, relevant project regression, sensitive-data inspection and isolated commits. Required proofs include review-change conflicts and rollback, no legacy activation, immutable policy execution, UTC/database deadlines, shared-lock acceptance/lifecycle races, pause/restart/resume remaining delay, exactly-once signal use/activation/outbox/delivery, inactive backup visible failure without replacement, scope/privacy/authorization negatives, and connected system scenarios D–I. Test both decline and unavailable and both sides of the acceptance/activation lock ordering.

Run the full pinned backend/web/PostgreSQL/OpenAPI/security/browser/container/fresh-migration/demo-reset gate and report exact test counts and commands. Do not reuse Phase 8.5 results as Phase 9 verification. Technical completion and human acceptance are separate. No Phase 10, push, tag or production approval is implied by this design.

Production trigger rules, intervals, retries, hierarchy, directory freshness/on-call authority, recipient eligibility, override roles, acceptance/transfer semantics, lifecycle authority, manual fallback routing, privacy, retention, security, identity, hosting, provider contracts and integrations all remain `REQUIRES_HOSPITAL_DECISION`.
