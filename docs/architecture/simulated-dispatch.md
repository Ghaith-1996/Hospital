# Phase 7 Simulated Dispatch

## Status and boundary

Phase 7 is a local, simulation-only dispatch worker for the fictional alert workflow. It is not a hospital communications system and does not authorize clinical escalation, provider use, or production deployment. The worker is allowed only when `SimulationDispatch:Enabled` is true in `Development` or `Test`; startup fails closed in `Staging` and `Production`.

The implementation deliberately excludes real SMS, voice, secure-message, FHIR, SCIM, Graph, Entra, hospital database, callback, doctor-response, live-screen, and escalation behavior. Any production choice about provider contracts, credentials, callback signatures, retry service levels, escalation, or operational ownership is `REQUIRES_HOSPITAL_DECISION`.

## Processing flow

```text
Phase 6 exact confirmation
        |
        v
identifier-only AlertDispatchRequested outbox row
        |
        v
PostgreSQL claim: FOR UPDATE SKIP LOCKED
        |
        v
lease owner + UTC expiry + strict payload validation
        |
        v
organization-scoped alert/policy/recipient/endpoint snapshot
        |
        v
typed SecureMessage / SMS / Voice simulation port
        |
        v
normalized synthetic provider events
        |
        v
durable attempt + event + audit state
        |
        +--> complete
        +--> bounded retry with visible queued state
        +--> terminal failure visible to the operator
```

The payload contains only the confirmed alert identifier and draft version. It cannot supply an organization, user, role, practitioner, endpoint, message, policy, or channel. The worker obtains those values from organization-scoped durable records and the exact confirmation snapshot. Protected endpoint ciphertext is used only as a stored value; the simulation worker does not decrypt it and dispatches only against synthetic endpoint labels.

## Lease ownership and restart safety

Claims are serialized by PostgreSQL row locking and `SKIP LOCKED`. A lease records an opaque worker owner and UTC expiry. Only the current owner may complete, retry, or fail a message. An expired `Processing` row is eligible for reclamation by another worker. Stable per-recipient/channel attempt keys and organization-scoped uniqueness prevent a restart or duplicate execution from creating a second durable attempt for the same attempt number.

The worker completes an outbox row only after attempts, delivery events, alert state, and sanitized audit metadata are persisted. Validation failures are terminal and visible. Provider-outage scenarios use bounded retry/backoff and then become visible failure; no retry policy can create escalation or a real provider call.

## Typed simulation ports

The worker depends on `INotificationChannel` and `INotificationStatusNormalizer`, not provider SDKs. SecureMessage, SMS, and Voice ports return deterministic synthetic references and event sequences for these explicit scenarios:

- `ImmediateSuccess`
- `DelayedDelivery`
- `SmsFailure`
- `VoiceNoAnswer`
- `ProviderOutage`
- `DuplicateCallback`
- `OutOfOrderCallback`

Scenario controls are Administrator-only and available only in `Development` or `Test`. They are organization-scoped and affect only the selected channel. The adapter emits generic `SIMULATION:` wake-up text and allowlisted synthetic metadata. No endpoint value, clinical body, patient reference, or raw provider payload is sent anywhere.

Delivery events are unique by `(organization_id, provider_event_id)`. Duplicate events are ignored after the first durable record. A late event cannot regress an attempt from `Delivered` to `Submitted` or otherwise move durable state backward. `Submitted`, `Delivered`, `Failed`, `Opened`, `Acknowledged`, and responsibility acceptance remain separate dimensions; Phase 7 implements only the delivery-attempt dimensions.

## Safe status projection

`GET /api/alerts/{alertId}/delivery` is authorized by server-side identity and organization scope. It returns alert version/state, outbox processing state, and operational attempt fields: recipient-selection identifier, channel, attempt number, simulation provider name, delivery status, opened-state marker, UTC timestamps, and safe failure category. It does not return approved-message ciphertext, decrypted contact endpoints, practitioner contact values, source text, clinical content, or raw event metadata.

The status projection is not a live screen and does not imply that delivery means opening, acknowledgement, responsibility acceptance, resolution, or escalation stop. Those behaviors remain out of scope and `REQUIRES_HOSPITAL_DECISION` where applicable.

## Phase gate

Phase 7 is ready for owner review only when focused unit tests, PostgreSQL/Testcontainers worker and authorization tests, frontend checks, full `test-all.ps1`, safety scans, and fresh-clone migration/seed verification pass. A missing Docker engine or other environment prerequisite is recorded as a verification limitation, not treated as a passing gate. No `phase-7` tag is created by implementation work alone.
