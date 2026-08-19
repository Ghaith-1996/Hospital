# ADR-0003: Require Explicit Human Confirmation Before Dispatch

- Status: Mandatory safety decision.
- Date: 2026-08-19.
- Deciders: User-requested project rule; hospital workflow approval remains required for production.

## Context

The platform handles urgent clinician communication. A model, transcription provider, directory sync, browser, worker, or notification provider must not create the appearance that it accepted clinical responsibility or chose the final recipient.

## Decision

No alert dispatch can occur without an authenticated authorized human explicitly confirming the exact alert draft version and exact recipients/channels shown in the review step.

The confirmation command must validate and persist atomically:

- exact draft version;
- approved message and operator-selected urgency;
- all required fields;
- every critical number and unit confirmation;
- manually selected recipients and channels;
- referenced policy versions;
- state transition;
- audit event; and
- `AlertDispatchRequested` outbox message.

Editing the source, approved content, critical values/units, urgency, recipients, channels, or policy reference invalidates approval and requires reconfirmation. The operation is idempotent and uses optimistic concurrency.

## Consequences

Positive:

- Human intent is explicit and auditable.
- AI and provider outputs cannot directly mutate approved content or dispatch.
- Double submission and stale drafts can be handled safely.

Trade-offs:

- The workflow is intentionally slower than autonomous dispatch.
- Usability must make exact-version confirmation clear under time pressure.
- Hospital-approved roles, escalation, and fallback remain necessary.

## Guardrails

- The server enforces the invariant; the UI only communicates it.
- A background worker cannot promote an unconfirmed draft.
- Recipient search cannot add a hidden recipient.
- No AI output is authoritative.
- Every critical number and unit is individually confirmable.

## Not decided here

The hospital's authorized roles, workflow trigger, allowed urgency labels, required template fields, resolution/cancellation permissions, and manual fallback are `REQUIRES_HOSPITAL_DECISION`.

