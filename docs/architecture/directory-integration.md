# Directory and On-Call Integration

Status: Phase 4 simulation CSV adapter with review corrections. Only fictional CSV is in scope for this boundary. No hospital directory or scheduling system may be connected from these documents.

## Integration principle

The directory identifies potential recipients; it does not decide clinical responsibility. The operator manually selects the final recipients. AI may suggest a specialty only and may not select or pre-check a practitioner.

The product directory is an **integration boundary**. CSV is the first adapter. Later SCIM, Graph, FHIR, scheduling systems, and restricted SQL views must normalize into the same practitioner, role, contact, on-call, and source-record model.

## Simulation source

Use a fictional CSV fixture with:

- Synthetic organization, site, department, practitioner, role, specialty, and endpoint references.
- At least two sites, three departments, twelve practitioners, similar names, two inactive rows, missing optional endpoint, primary/backup fixtures, and one stale record.
- A visible source timestamp and freshness status. The simulation CSV fixture is `fixtures/simulation/directory-harborview.csv`.
- No real names, IDs, endpoints, schedules, or phone numbers.

The import is previewable, validates rows before applying changes, reports conflicts without sensitive payloads, and never calls a real external system.

## Strict simulation CSV contract

The CSV adapter is intentionally strict at its input boundary. It rejects duplicate headers, malformed quoted fields, and rows whose column count does not match the header. Required values, booleans, enum values, UTC timestamps, `SIM-` identifiers, and location codes are validated with a row reference. SMS and voice values must be complete fictional `555` numbers; secure-message values must use `sim-secure://`; endpoint labels must be synthetic `SIM-` values.

The adapter returns normalized practitioners, roles, protected-contact inputs, on-call assignments, source metadata, blocking errors, and non-blocking warnings through `IDirectorySourceAdapter`. Error and warning responses contain only safe issue codes, row numbers, synthetic source identifiers, and messages; protected endpoint values are not echoed.

The CSV simulation policy also uses an explicit fictional practitioner/name, specialty, and role catalog. This allowlist is deliberately a test-data boundary, not a canonical hospital role model or a production claim about real practitioners.

## Safe identity matching

Never match practitioners solely by display name. Reconciliation requires a stable synthetic source identifier and records the source system, source record ID, source version/timestamp, payload hash, last-seen time, and mapping result.

The simulation matcher uses this order only:

1. `(organization, source_system, source_record_id)`
2. `(organization, simulation_code)`

Same first and last name with a different source identifier is a warning, not a merge. Production source identifiers, mapping authority, conflict resolution, deactivation behavior, and merge policy are `REQUIRES_HOSPITAL_DECISION`.

Roles, protected contact endpoints, and on-call assignments carry their own source system and source record ownership. A CSV reconciliation replaces only its `SIM-CSV` children and leaves another adapter's contribution intact. This keeps CSV from becoming a destructive replacement for the shared directory model.

## Simulation freshness

The CSV `freshness_status` column is an explicit simulation field (`current` or `stale`). This prototype does not invent a production freshness window. The production freshness threshold, stale-data selection behavior, deactivation timing, and emergency override are `REQUIRES_HOSPITAL_DECISION`.

## Practitioner display and selection

Search results must show enough disambiguation for similar names:

- Display name.
- Specialty.
- Department.
- Site.
- Role.
- Synthetic identifier suffix.
- Active/inactive state.
- Directory source and last synchronization time.
- Stale/freshness status.
- On-call assignment source and timestamp when available.

The operator must explicitly select each recipient. No provider, AI service, worker, sync job, or background rule may add a recipient invisibly.

## Preview, apply, and UI safety

Preview loads only the authenticated organization’s catalog and produces a plan without writing practitioners, source records, sync runs, or audit events. Apply re-plans the submitted source and rejects every blocking conflict before opening a transaction; successful writes persist the normalized practitioner, role, protected endpoint, on-call, and source-record state atomically with a sanitized sync record.

The API derives user and organization context from the server-created authenticated principal. Caller-supplied headers, query values, and form fields cannot select a user, organization, or role. Each clean preview includes a non-secret freshness token derived from the authenticated organization, adapter, uploaded payload, and scoped catalog revision; apply requires it and rejects a stale or missing preview before opening a transaction. The web import page clears a prior preview when the selected file changes and disables Apply until a clean preview exists for the current selection; these controls are usability safeguards only, not authorization boundaries.

## Staleness and inactive records

The simulation visibly flags stale data and blocks inactive practitioners from selection. Search reports an on-call assignment only when its UTC window is currently active (`starts_at_utc <= now < ends_at_utc`); employment activity and on-call status remain separate. The production freshness threshold, stale-data selection behavior, deactivation timing, and emergency override are `REQUIRES_HOSPITAL_DECISION`.

## Planned integration order

The attached master plan proposes this sequence, subject to human approval:

1. Fictional CSV import.
2. SCIM 2.0 users/groups.
3. Microsoft Graph delta synchronization.
4. FHIR R4 `Practitioner` and `PractitionerRole`.
5. Scheduling/on-call adapter.
6. Restricted read-only SQL view only as an approved legacy fallback.
7. Hospital-side connector service.

No real adapter may be implemented until the hospital supplies source-of-truth owners, data dictionary, stable identifiers, sandbox, authentication, network rules, de-identified sample payloads, freshness threshold, deactivation rules, conflict resolution, and support contacts. Each missing item is `REQUIRES_HOSPITAL_DECISION`.

## Future connector boundary

A future connector should run inside an approved hospital network, make outbound authenticated connections, keep source credentials inside that network, send only minimum required fields, support safe full/incremental synchronization, report freshness and failures, use mutual TLS where approved, and avoid exposing payloads during remote diagnostics.

The connector service account must be least-privileged and read-only where a legacy view is approved. It must not access patient tables or broad HR schemas. Approval of this design is `REQUIRES_HOSPITAL_DECISION`.

