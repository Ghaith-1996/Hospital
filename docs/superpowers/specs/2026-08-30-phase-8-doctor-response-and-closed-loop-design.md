# Phase 8 Doctor Response and Closed Loop Design

Status: Approved simulation-only design with compliance-correction additions. This is not a hospital-approved response, responsibility-transfer, escalation, lifecycle, or fallback policy.

## Goal

Allow one explicitly linked fictional practitioner identity to view alerts addressed to that practitioner and record opened, acknowledgement, call-unit request, acceptance, decline, or unavailability while an authorized operator observes a safe recipient-by-recipient status projection and can close the simulation alert through explicit lifecycle actions.

## Boundary

Phase 8 is enabled only in `Development` or `Test` and fails closed elsewhere. It adds no real provider, external callback, hospital identity/directory connection, automated escalation, production transfer/release workflow, AI, or Phase 9 behavior. It does add a simulation-only call-unit request, operator cancel/resolve actions, and a non-routing manual-fallback placeholder so the closed loop has an explicit safe endpoint. Missing production response, responsibility, deprovisioning, retention, lifecycle, fallback, and escalation semantics remain `REQUIRES_HOSPITAL_DECISION`.

## Identity and authorization

`PractitionerUserLink` is the sole user-to-practitioner authority. It is organization-scoped and references durable internal user and practitioner identifiers. The API derives user, organization, roles, and practitioner from the authenticated principal plus this server-side link. Handles, display names, request bodies, query strings, and headers cannot select or override a practitioner.

The simulation links `sim-practitioner-riley` explicitly to the fictional `SIM-PRAC-0108` practitioner. The display labels are aligned for clarity, but tests intentionally prove that names are not the mapping key. Practitioner routes require the Practitioner/Physician policy and an active link. Operator live status and lifecycle actions require the corresponding server-side Operator, Administrator, ClinicalSupervisor, or SystemAdministrator policy; those roles cannot impersonate a practitioner response.

## Response semantics

Responses are scoped by organization, alert, confirmed alert version, and practitioner—not by channel—because one practitioner may be selected on several channels.

- SecureMessage open is recorded on the practitioner's SecureMessage delivery attempt with a UTC timestamp. SMS and Voice remain `NotApplicable` for opened state.
- Acknowledgement is one independent, idempotent event. It does not accept responsibility.
- Exactly one terminal simulation disposition is allowed: `Accepted`, `Declined`, or `Unavailable`.
- A `CallUnitRequested` response is a separate, non-terminal, idempotent event with an allowlisted reason code. It never pages or contacts a real unit.
- Acceptance creates one responsibility assignment in the same transaction. It does not automatically resolve the alert.
- Decline and unavailable remain visible. Phase 8 does not schedule escalation or reassignment.
- A repeated identical command returns the persisted result. A different request using the same key, or a conflicting terminal disposition, returns a safe conflict.
- Changing, releasing, or transferring a disposition is excluded pending `REQUIRES_HOSPITAL_DECISION`.

Free-text reason content is not accepted. The server validates an allowlisted simulation reason code for the selected action.

## Data flow

The practitioner inbox resolves the authenticated link, then queries only `Active` alerts whose exact confirmed recipient snapshot contains that practitioner. Summary responses omit protected message content. Detail responses decrypt only the approved message and exact confirmed critical fields needed by the addressed practitioner; they never expose source ciphertext, contact endpoints, encryption metadata, or raw provider metadata.

Opened and response commands validate the exact confirmed version and recipient membership. One database transaction persists the idempotency record, recipient response, optional responsibility assignment, and sanitized audit event. Database uniqueness is the final concurrency guard.

The operator live query is organization-scoped. It projects alert/outbox state, recipient identity and disambiguators, channel attempts, opened timestamps, acknowledgement, call-unit request, terminal disposition, responsibility state, safe failures, and UTC timestamps. It exposes `Resolve` only for an Active alert with an unreleased responsibility assignment at the confirmed version, and `Cancel` for an Active alert. The browser polls this query and labels the result as refreshed status rather than guaranteed real-time monitoring. Both actions are exact-version, idempotent, human-authorized simulation commands.

## Error and privacy behavior

Unauthenticated requests return 401 and wrong-role requests return 403. An unaddressed or cross-organization alert returns 404. Stale versions, reused idempotency keys with a different request, conflicting terminal dispositions, and lifecycle precondition failures return RFC 7807 conflicts with recovery guidance. Errors and audit metadata use identifiers, counts, action names, and safe reason codes only. A failed delivery renders a manual-fallback placeholder containing no real route or contact value and marked `REQUIRES_HOSPITAL_DECISION` for production meaning.

## Verification

Tests cover explicit identity mapping, multi-channel recipient collapse, organization isolation, unauthenticated/wrong-role negatives, opened capability semantics, acknowledgement/acceptance/call-unit separation, terminal conflicts, idempotency races, transaction rollback, protected-content non-disclosure, lifecycle preconditions and replay, accessible UI status distinctions, and full PostgreSQL/browser/fresh-clone verification.
