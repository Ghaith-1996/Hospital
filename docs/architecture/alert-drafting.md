# Simulation Alert Drafting

Status: Phase 5 implementation complete and ready for project-owner review. This is a fictional workflow slice and is not approved for clinical use.

## Scope

The current slice supports an authenticated Operator or Administrator creating and editing a typed alert draft with a synthetic patient reference, simulation location, urgency label, typed source, and four required SBAR fields: Situation, Background, Assessment, and Recommendation. Drafts can be submitted to `PendingConfirmation` only after required content is present and every recorded critical number/unit has been explicitly confirmed by an authenticated human.

The API derives organization and user identity from the authenticated server principal. Request JSON cannot supply a trusted organization, user, role, recipient, or dispatch instruction. Practitioner identities are denied draft commands by server authorization policy.

## Protected content and concurrency

Typed source and serialized SBAR content use the existing `ISensitiveDataProtector` with separate purposes and separate persisted properties. The original operator-entered source content is persisted separately from the structured SBAR representation and is never silently overwritten by normalization or critical-field confirmation. The API returns authorized simulation content for the compose screen, never ciphertext or protected-value internals. General API logs and safe error responses are tested with synthetic sentinels to ensure they do not contain the patient reference, typed source, or complete SBAR payload.

Every edit names the expected `DraftVersion`; stale edit, critical-field confirmation, and submission commands are rejected with a safe conflict response and recovery guidance. Any source, SBAR, location, urgency, critical-value, or unit edit increments the version. The complete critical-field list is recreated as unresolved for the new version, so no confirmation from an earlier version remains current.

Critical-field confirmations persist the exact recorded value, unit, normalized value explicitly confirmed by the operator, confirming user, timestamp, and alert draft version. A confirmation request cannot substitute a different recorded value or unit, cannot rewrite a completed confirmation within the same version, and cannot confirm a field absent from the current version. The current slice can display, edit, and confirm fields recorded in the draft; it does not infer, diagnose, assign urgency, select recipients, or dispatch.

## Routes in this phase

- `POST /api/alerts/drafts`
- `GET /api/alerts/{alertId}`
- `PATCH /api/alerts/{alertId}`
- `POST /api/alerts/{alertId}/field-confirmations`
- `POST /api/alerts/{alertId}/submit-for-confirmation`

Recipient selection, review confirmation, transactional outbox, simulated channels, provider adapters, and hospital integrations are later-phase work and remain `REQUIRES_HOSPITAL_DECISION` where production behavior would be involved.

No alert can reach `DispatchQueued` through any Phase 5 endpoint. The furthest Phase 5 submission can advance is `PendingConfirmation`; the human dispatch-confirmation command is not exposed by the Phase 5 API or application service.

## Phase 5 decision status

Typed drafting, protected SBAR storage, critical-field confirmation, and optimistic concurrency are simulation-only implementation controls. They do not approve a hospital urgency vocabulary, required clinical template, critical-value catalog, privacy conclusion, retention rule, or production workflow. Those matters remain `REQUIRES_HOSPITAL_DECISION`.
