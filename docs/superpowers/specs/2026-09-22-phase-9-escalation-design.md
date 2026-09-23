# Phase 9 — deterministic DEMO escalation

Implementation authorized by the supplied project-owner plan and 2026-09-22 request. Baseline: Phase 8.5 reintegration, `9e312b2`. This does not imply retrospective phase acceptance or hospital approval.

## Approved boundary

Development/Test only. Reuse the modular monolith, PostgreSQL, existing Worker, transactional outbox, simulated channels, response/responsibility records and connected live screen. No real providers, external callbacks, clinical inference, identity/directory integration, AI or Phase 10. All examples are fictional. Browser timers and polling only present server state.

## Exact approval

Review exposes the exact policy ID/version, step delays, future practitioner/role/channel snapshots, directory/on-call evidence and deterministic plan revision. Confirmation binds all of them to the exact alert version, recomputes the revision inside its transaction, and rejects changed evidence. Persist the approved snapshot atomically with confirmation, audit and the existing identifier-only dispatch request. Policy versions and confirmed snapshots are immutable. Old alerts without snapshots remain escalation-disabled; no inferred or backfilled policy.

The DEMO plan uses explicit synthetic backup-on-call evidence from the alert's organization and location, excluding manually selected practitioner/channel pairs. It never ranks clinicians by clinical content. All future recipients are visible before approval. Empty steps are visible and exhaust safely; no replacement lookup occurs at expiry. These are simulation assumptions, not production routing rules.

## Durable processing

Create one organization-scoped run per confirmed alert version after it becomes Active. Persist exact version, policy identity, current step, UTC due time, lease ownership/expiry, pause remaining delay, stop reason and append-only identifier-only events. Due decisions use PostgreSQL time. Acquire the same alert mutation lock used by response/lifecycle commands before rechecking durable state and activating recipients. Recover expired claims and serialize concurrent workers. Activation and the new identifier-only `EscalationDispatchRequested` outbox row commit together; only its selection IDs dispatch, never the original manual recipients. No draft-version increment: the activated snapshot was approved already.

Evaluation precedence: Resolved/Cancelled; active exact-version responsibility assignment; paused; new decline/unavailable; deadline; wait. Delivery, opening and acknowledgement never stop escalation. Acceptance wins if its assignment is durable when the locked run is processed. Consume decline/unavailable signals once so a prior response cannot exhaust all future steps. All steps exhausted without responsibility exposes the existing manual-fallback warning. Failures are durable and operator-visible.

## Human controls and projection

Pause/Resume require the existing lifecycle authorization, organization, exact confirmed version, idempotency key and allowlisted reason code. Persist audit and timeline together. Pause stores the nonnegative remaining database-clock delay; resume restores it, including immediate eligibility when already due. No permanent stop action. Resolve/Cancel retain their existing controls.

The live projection exposes DEMO label, exact policy/version, state, step, UTC due time, safe events, escalation recipient provenance and permitted controls. No clinical body, patient reference, endpoint, ciphertext or raw provider payload appears in events/logs/audit/outbox/live escalation data.

## Verification and unresolved decisions

Use test-first slices, real PostgreSQL/Testcontainers, full retained regressions, API authorization/idempotency/non-disclosure tests, connected frontend tests and system scenarios D–I including restart/two-worker races. Add migrations; never edit historical migrations. Record RED/GREEN evidence and all unexecuted gates honestly.

`REQUIRES_HOSPITAL_DECISION`: production timing, escalation authority, eligible recipients and backup hierarchy, policy ownership, directory/on-call freshness, stop/response semantics, pause/resume authority, fallback and clinical policy, privacy/retention, hosting, identity and integrations. Existing DEMO delays are simulation fixtures only.
