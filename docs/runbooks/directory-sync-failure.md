# Directory synchronization failure

## Purpose
Recover a rejected fictional CSV import or recorded synchronization failure while preserving exact recipient identity and freshness evidence.

## Scope
The simulation CSV adapter, directory search, latest safe sync status and import preview/apply flow. No Entra, SCIM, FHIR or live directory.

## Detection
A blocking preview/apply result, DirectoryStale, or DirectorySynchronizationFailed indicates review is needed. Authorized DirectoryReader roles can GET /api/v1/directory/sync-status with their existing session; it returns fixed status/source vocabulary, timestamps and counts. No sync run means NotRecorded. A rejected validation/preview is not a completed sync and retains the last successful status.

## Safety impact
Stale means the source evidence is marked stale; inactive means the practitioner is not active/selectable. They are different conditions. Names alone cannot establish recipient identity.

## Immediate actions
Read the import UI's safe validation codes and latest sync status. Inspect source system (SIM-CSV for imports, SIM-DIRECTORY for seeded evidence), source record identifier, active state, freshness and current selection revision using authorized directory tools and the fictional input fixture. Stop recipient selection if the evidence is not sufficient.

## What NOT to do
Do not match solely by name, edit internal IDs manually, reactivate without source evidence, ignore freshness warnings, copy raw directory payloads to logs, or assume a failed apply changed the directory.

## Diagnosis
Use the checked-in fixtures/simulation/directory-harborview.csv format. Verify source-system/source-record identity, department/site references, known role titles and endpoints in the private fictional input. Preview errors block apply. An absent or stale preview token requires a fresh preview; do not bypass it. Unknown source vocabulary in the safe status is shown as unknown and requires investigation in the controlled source, not a guessed match.

## Recovery
Correct the fictional source using valid evidence. As an authorized directory administrator, preview again, review reconciliation results, then apply using that current preview. Reselect/review recipients in the application if revisions changed. Confirmation must use current evidence. No automatic replacement recipient is selected.

## Verification
The apply result succeeds with a sync run identifier; latest safe status and counts reflect it. Search the affected fictional entries, distinguish active/freshness state, verify source identifiers and current revision, and repeat the exact human review before dispatch. Confirm directory.import.applied in authorized audit.

## Evidence to preserve
Validation codes, safe counts/status, UTC timestamps, opaque sync/correlation IDs and audit action. No input CSV, endpoint values, practitioner contact details, credentials or raw errors in incident evidence.

## Exit criteria
A valid import is applied, affected records match source evidence, unsafe selections are blocked, and any remaining stale/inactive conditions are visible. A success status does not authorize ignoring individual freshness warnings.

## Production decisions
REQUIRES_HOSPITAL_DECISION: directory authority, directory outage fallback, freshness thresholds, support/on-call ownership, production identity provisioning, incident severity and retention.
