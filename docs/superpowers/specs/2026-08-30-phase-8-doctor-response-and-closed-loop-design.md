# Phase 8 Doctor Response and Closed Loop Design

Status: Approved simulation-only design. This is not a hospital-approved response, responsibility-transfer, or escalation policy.

## Goal

Allow one explicitly linked fictional practitioner identity to view alerts addressed to that practitioner and record opened, acknowledgement, acceptance, decline, or unavailability while an authorized operator observes a safe recipient-by-recipient status projection.

## Boundary

Phase 8 is enabled only in `Development` or `Test` and fails closed elsewhere. It adds no real provider, external callback, hospital identity/directory connection, escalation, resolution, transfer/release workflow, call-unit request, AI, or Phase 9 behavior. Missing production response, responsibility, deprovisioning, retention, and escalation semantics remain `REQUIRES_HOSPITAL_DECISION`.

## Identity and authorization

`PractitionerUserLink` is the sole user-to-practitioner authority. It is organization-scoped and references durable internal user and practitioner identifiers. The API derives user, organization, roles, and practitioner from the authenticated principal plus this server-side link. Handles, display names, request bodies, query strings, and headers cannot select or override a practitioner.

The simulation links `sim-practitioner-riley` explicitly to the fictional `SIM-PRAC-0108` practitioner. The display labels are aligned for clarity, but tests intentionally prove that names are not the mapping key. Practitioner routes require the Practitioner policy and an active link. Operator live status requires Operator or Administrator; those roles cannot impersonate a practitioner response.

## Response semantics

Responses are scoped by organization, alert, confirmed alert version, and practitioner—not by channel—because one practitioner may be selected on several channels.

- SecureMessage open is recorded on the practitioner's SecureMessage delivery attempt with a UTC timestamp. SMS and Voice remain `NotApplicable` for opened state.
- Acknowledgement is one independent, idempotent event. It does not accept responsibility.
- Exactly one terminal simulation disposition is allowed: `Accepted`, `Declined`, or `Unavailable`.
- Acceptance creates one responsibility assignment in the same transaction. It does not resolve the alert.
- Decline and unavailable remain visible. Phase 8 does not schedule escalation.
- A repeated identical command returns the persisted result. A different request using the same key, or a conflicting terminal disposition, returns a safe conflict.
- Changing, releasing, or transferring a disposition is excluded pending `REQUIRES_HOSPITAL_DECISION`.

Free-text reason content is not accepted. The server derives an allowlisted simulation reason code from the selected action.

## Data flow

The practitioner inbox resolves the authenticated link, then queries only `Active` alerts whose exact confirmed recipient snapshot contains that practitioner. Summary responses omit protected message content. Detail responses decrypt only the approved message and exact confirmed critical fields needed by the addressed practitioner; they never expose source ciphertext, contact endpoints, encryption metadata, or raw provider metadata.

Opened and response commands validate the exact confirmed version and recipient membership. One database transaction persists the idempotency record, recipient response, optional responsibility assignment, and sanitized audit event. Database uniqueness is the final concurrency guard.

The operator live query is read-only and organization-scoped. It projects alert/outbox state, recipient identity and disambiguators, channel attempts, opened timestamps, acknowledgement, terminal disposition, responsibility state, safe failures, and UTC timestamps. The browser polls this query and labels the result as refreshed status rather than guaranteed real-time monitoring.

## Error and privacy behavior

Unauthenticated requests return 401 and wrong-role requests return 403. An unaddressed or cross-organization alert returns 404. Stale versions, reused idempotency keys with a different request, and conflicting terminal dispositions return RFC 7807 conflicts with recovery guidance. Errors and audit metadata use identifiers, counts, action names, and safe reason codes only.

## Verification

Tests cover explicit identity mapping, multi-channel recipient collapse, organization isolation, unauthenticated/wrong-role negatives, opened capability semantics, acknowledgement/acceptance separation, terminal conflicts, idempotency races, transaction rollback, protected-content non-disclosure, accessible UI status distinctions, and full PostgreSQL/browser/fresh-clone verification.
