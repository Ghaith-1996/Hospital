# Recipient Selection and Exact Review

## Status and authority

This document records the Phase 6 simulation behavior that precedes the Phase 7 worker. It does not create hospital policy or authorize clinical use. Production recipient eligibility, on-call ownership, directory freshness limits, communication channels, final confirmer roles, and confirmation wording are `REQUIRES_HOSPITAL_DECISION`.

The Phase 6 implementation uses fictional directory and patient data exclusively. Phase 7 is separately scoped to consume the resulting identifier-only outbox item through deterministic local simulation adapters; this document does not authorize provider calls or recipient changes by the worker.

## Scope

Phase 6 adds:

- manual practitioner search by name, specialty, department, site, and on-call status;
- complete recipient-set replacement with an exact draft-version snapshot;
- protected operator-approved message content separate from source and SBAR records;
- an exact review projection;
- authenticated, idempotent human confirmation;
- sanitized audit records; and
- atomic creation of one identifier-only outbox item when the reviewed version is confirmed.

Phase 6 does not lease or process outbox items, create delivery attempts, call channels or providers, retry, escalate, receive callbacks, expose a live screen, acknowledge, or accept responsibility. Those behaviors are separately constrained by the Phase 7 simulation-only dispatch boundary. No provider code can run through a Phase 6 endpoint.

## Preserved content records

The original operator-entered source, structured SBAR representation, and operator-approved message remain separately protected records. Formatting or normalization never silently overwrites the original source. Each source revision is retained in immutable `alert_source_revisions` history. Setting or editing the approved message increments `DraftVersion`, invalidates any earlier confirmation state, and recreates the current critical fields as unresolved for the new version.

The approved message is written by the authenticated operator in Phase 6. AI does not generate, approve, or alter it.

## Manual directory selection

Directory search is organization-scoped from the authenticated principal. Client-supplied organization or user identifiers are ignored. Search may filter the fictional directory by name, specialty, department, site, and current simulated on-call status. Similar names remain distinguishable through specialty, department, site, role, and simulation code.

Search returns only safe selection data. It never returns protected endpoint values. An active endpoint contributes an available channel kind such as `SecureMessage`, `Sms`, or `Voice`.

Inactive practitioners are never selectable. For Phase 6 simulation, an active stale record remains manually selectable only with a visible stale warning and timestamp. Whether stale data must block production selection is `REQUIRES_HOSPITAL_DECISION`.

No result is preselected, ranked as a recommendation, added by a background task, or selected by AI. The authenticated human chooses every practitioner and channel.

### Directory revision

Every search result includes a deterministic, non-secret `selectionRevision`. It is derived from the organization-scoped practitioner's stable identifier, active state, role assignment, safe source timestamps, on-call snapshot, and available channel kinds. It excludes names from the hash input when unnecessary and always excludes endpoint values.

The recipient command sends the revision that the operator reviewed. The server recomputes it inside the command. A mismatch returns HTTP 409 with safe reload-and-reselect guidance. The accepted selection stores the revision, source timestamp, and displayed on-call label so the final review proves what was selected.

## One versioned recipient command

Recipients are replaced as one complete set:

```text
PUT /api/v1/alerts/{alertId}/recipients
{
  "expectedVersion": 4,
  "recipients": [
    {
      "practitionerId": "fictional-guid",
      "practitionerRoleId": "fictional-guid",
      "channel": "SecureMessage",
      "directoryRevision": "sha256-base64url"
    }
  ]
}
```

The server validates the entire set before mutation. It rejects duplicate practitioner/channel pairs, foreign-organization or inactive practitioners, roles that do not belong to the practitioner and organization, unavailable channels, and changed directory revisions. An empty set explicitly clears the recipients.

One successful replacement increments `DraftVersion` exactly once, regardless of recipient count. Accepted rows all carry the new version. Earlier rows remain historical. The database uniqueness boundary is `(alert_id, alert_version, practitioner_id, channel)`.

Because recipient edits create a new exact alert version, all critical fields for the new version are unresolved. Content and approved-message edits copy the current recipient snapshot forward to the new version; recipient replacement writes the submitted complete set instead. No earlier critical confirmation or final confirmation applies to a new version.

## Exact review projection

`GET /api/v1/alerts/{alertId}/review` returns an immutable view of the current version. It includes:

- synthetic patient reference, simulation location, and urgency label;
- the separately protected approved message;
- every current critical value and unit with exact-version confirmation evidence;
- selected practitioners, optional role labels, channels, selection timestamps, safe directory source timestamps, revisions, displayed on-call status, and `selectionSource` (`Manual`, `TeamExpansion`, or `EscalationPolicy`);
- the `DEMO` escalation and notification policy versions; and
- the draft version used by the confirmation command.

The review query is allowed only when the alert is `PendingConfirmation`, has a non-empty approved message, has at least one current recipient, and every current critical field is confirmed for its exact normalized value, unit, and version. It returns a safe conflict if any prerequisite changed.

## Idempotent confirmation transaction

`POST /api/v1/alerts/{alertId}/confirm` requires `Idempotency-Key` and this body:

```json
{
  "expectedVersion": 7
}
```

The request hash is canonicalized from operation name, authenticated organization, alert identifier, and expected version. The key is scoped by organization and operation.

For a new key, one PostgreSQL transaction:

1. loads the organization-scoped alert with current recipient and critical-field snapshots;
2. proves the state and exact version still match the review;
3. proves the approved message, recipients/channels, critical values/units, and `DEMO` policy versions are complete;
4. records the authenticated confirmer and transitions to `DispatchQueued`;
5. appends one sanitized confirmation audit event;
6. stores the idempotency request hash and replay-safe response;
7. inserts one `AlertDispatchRequested` outbox item containing only `alertId` and `draftVersion`; and
8. commits all changes atomically.

The outbox idempotency key is stable for alert and version. Its payload contains no patient, clinical, message, practitioner, role, contact, or endpoint content. Creating this item in Phase 6 is part of the confirmation safety transaction; Phase 7 owns all processing.

The same key and request hash returns the stored success response without repeating the transition, audit, or outbox insert. The same key with a different hash returns HTTP 409. Concurrent requests are resolved by PostgreSQL uniqueness and optimistic concurrency, then replay or conflict safely.

If any write fails, the transaction rolls back and the alert does not remain `DispatchQueued` without its audit, idempotency result, and outbox item.

## Authorization and organization isolation

The Phase 6 simulation authorizes Operator, Administrator, ClinicalSupervisor, and SystemAdministrator identities for recipient editing, review, and confirmation according to the endpoint policy. Practitioner and anonymous requests are denied. Production role mapping and separation-of-duty requirements are `REQUIRES_HOSPITAL_DECISION`.

Every read and command derives `OrganizationId` and `UserId` from the authenticated principal. A foreign-organization alert or practitioner is returned as not found or rejected without disclosing its existence. Client body, query, or header values cannot override the server identity context.

## Logging and audit

General logs and exception messages contain correlation identifiers, alert identifiers, operation names, safe result categories, and versions only. Request bodies, source content, SBAR fields, approved messages, patient references, recipient names, and contact values are never logged.

Audit events record actor, organization, alert, action, UTC time, version, recipient count, channel kinds, `DEMO` policy version identifiers, and correlation ID. They do not record clinical text, approved content, patient identifiers, practitioner names, or contact details.

Synthetic sentinel tests scan captured API logs, RFC 7807 responses, audit metadata, idempotency response storage, and outbox payloads for forbidden content.

## API surface

- `GET /api/v1/directory/practitioners`: extend safe filters and selection metadata.
- `PUT /api/v1/alerts/{alertId}/approved-message`: protect a manual approved message using `expectedVersion`.
- `PUT /api/v1/alerts/{alertId}/recipients`: replace the complete current recipient set using `expectedVersion`.
- `GET /api/v1/alerts/{alertId}/review`: return the exact confirmable review version.
- `POST /api/v1/alerts/{alertId}/confirm`: idempotently confirm the exact review version.

All errors use RFC 7807. Stale draft or directory revisions use HTTP 409 with reload guidance. Validation failures use HTTP 400 without echoing sensitive input.

## Web flow

The simulation flow is `/alerts/new` to `/alerts/{id}/compose` to `/alerts/{id}/recipients`, then back to compose to reconfirm the new version's critical values before `/alerts/{id}/review`. This return is required because a recipient edit increments the exact draft version and invalidates prior critical confirmations. The final control is deliberately labelled **Confirm and queue simulation alert**, displays the exact version, requires an explicit confirmation checkbox, disables during submission, and handles replay safely.

There is no Phase 6 `/live` experience. After success, the UI reports that the simulation alert is queued for the Phase 7 simulation dispatcher; it does not claim delivery.

## Phase gate

Phase 6 passes only when tests prove manual selection, exact versioning, confirmation invalidation, directory revision conflicts, negative authorization, organization isolation, log non-disclosure, idempotent double confirmation, atomic rollback, identifier-only outbox content, and absence of provider or dispatch processing. The implementation then stops for project-owner review.
