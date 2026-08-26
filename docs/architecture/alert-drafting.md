# Simulation Alert Drafting

Status: Phase 5 in progress. This is a fictional workflow slice and is not approved for clinical use.

## Scope

The current slice supports an authenticated Operator or Administrator creating and editing a typed alert draft with a synthetic patient reference, simulation location, urgency label, typed source, and four required SBAR fields: Situation, Background, Assessment, and Recommendation. Drafts can be submitted to `PendingConfirmation` only after required content is present and every recorded critical number/unit has been explicitly confirmed by an authenticated human.

The API derives organization and user identity from the authenticated server principal. Request JSON cannot supply a trusted organization, user, role, recipient, or dispatch instruction. Practitioner identities are denied draft commands by server authorization policy.

## Protected content and concurrency

Typed source and serialized SBAR content use the existing `ISensitiveDataProtector` with separate purposes. The API returns authorized simulation content for the compose screen, never ciphertext or protected-value internals. Every edit names the expected `DraftVersion`; a stale edit is rejected with a safe conflict response. Any source/SBAR/location/urgency edit increments the version and invalidates prior confirmation state.

Critical-field confirmations are versioned with the alert. The current slice can display and confirm fields seeded at draft creation; it does not infer, normalize, diagnose, assign urgency, select recipients, or dispatch.

## Routes in this phase

- `POST /api/alerts/drafts`
- `GET /api/alerts/{alertId}`
- `PATCH /api/alerts/{alertId}`
- `POST /api/alerts/{alertId}/field-confirmations`
- `POST /api/alerts/{alertId}/submit-for-confirmation`

Recipient selection, review confirmation, transactional outbox, simulated channels, provider adapters, and hospital integrations are later-phase work and remain `REQUIRES_HOSPITAL_DECISION` where production behavior would be involved.
