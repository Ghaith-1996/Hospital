# Notification provider outage

## Purpose
Diagnose and recover a simulated provider failure without duplicate alerts or hidden delivery failure.

## Scope
The existing simulation scenario administrator and bounded outbox worker only. There is no real SMS, voice, callback or external provider.

## Detection
Live status shows ProviderUnavailable, DeliveryFailed, DispatchDelayed, or escalation delay/exhaustion. Check the durable attempt status, original outbox and escalation outbox separately. Fixed operational logs and process-local counters corroborate evidence; they do not replace it.

## Safety impact
A recorded alert can remain durable while delivery fails. Provider acceptance, delivered, opened, acknowledgement and accepted responsibility remain distinct.

## Immediate actions
Refresh the alert's connected live screen. Verify the alert/confirmed version exists, dispatch and outbox states are visible, each attempt number is known, and the manual fallback indicator is visible. Review the bounded retry configuration in DispatchWorkerOptions and the exact confirmed Phase 9 plan. Use the existing simulation scenario controls only with an authorized administrator.

## What NOT to do
No unbounded retries, duplicate alerts as a workaround, manual database state edits, unconfirmed recipient changes, silent provider switching, or patient information in fallback SMS/voice. Do not interpret a process metric as proof of delivery or responsibility.

## Diagnosis
Check /health/live and /health/ready separately. A healthy API can correctly report delayed workers. Review the exact confirmed recipients, attempt status and failure category, outbox Pending/Processing/Processed/Failed state, and Phase 9 timeline. Inspect /admin/audit as Auditor/SystemAdministrator for dispatch.retry-scheduled, dispatch.failed, dispatch.delivery-event and actual escalation events. No raw provider response is needed.

## Recovery
Restore the configured simulated scenario using the existing administrator scenario endpoint/UI. Restart a stopped simulation worker with unchanged confirmed state and existing bounded settings. Pending work recovers using durable leases/idempotency. A terminal failed outbox is not silently reset or resent by Phase 10; preserve it and follow the approved fallback, REQUIRES_HOSPITAL_DECISION. Any later recipient change must use the human-confirmed application workflow. Escalation controls affect future steps according to the confirmed plan.

## Verification
Refresh live status, verify bounded attempt counts and absence of duplicate sends, and confirm failure/fallback remains visible when terminal. A recovered provider does not retroactively convert a failed attempt into success. Verify any new attempt/delivery evidence and responsibility independently.

## Evidence to preserve
Opaque correlation IDs, fixed warning codes, UTC times, attempt counts, state transitions and safe audit projection. Never preserve message bodies, endpoint values, raw callback payloads, credentials or screenshots of clinical content.

## Exit criteria
Processing state is known and stable, duplicate sends are absent, bounded recovery is verified, and unresolved terminal failures remain visible with an approved fallback decision pending. Technical recovery does not establish clinical resolution.

## Production decisions
REQUIRES_HOSPITAL_DECISION: communications vendor, provider outage fallback route/telephone, support/on-call owner, hospital escalation contact, severity mapping, alert thresholds, incident authority and retention.
