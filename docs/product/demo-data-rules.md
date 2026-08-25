# Demo Data Rules

Status: Mandatory simulation-data policy. These rules apply to Development, Test, screenshots, examples, fixtures, local databases, and issue reports.

## Absolute rule

Use fictional hospital, employee, doctor, patient, phone, and clinical data only. Do not copy, transform, anonymize, hash, truncate, or otherwise derive demo data from real records. Synthetic data must be generated as synthetic data from the start.

## Fictional simulation identity

The following names are examples for simulation fixtures only:

- Organization: `Fictional Harborview Simulation Hospital`.
- Sites: `North Wing Simulation Site`, `Riverside Annex Simulation Site`.
- Departments: `Fictional Emergency Care`, `Fictional Medicine`, `Fictional Surgery`.
- Practitioners: `Dr. Maya Chen`, `Dr. Rowan Patel`, `Dr. Jules Martin`, `Dr. Avery Brooks`, `Dr. Samira Nguyen`, and other generated names clearly marked as fictional.
- Operators: `Jordan Lee` and `Morgan Ellis`, both fictional seeded users. Phase 3 also seeds `Riley Cole` as a fictional practitioner user for development authentication.
- Patient references: `SIM-PAT-0001`, `SIM-PAT-0002`, and similar non-production identifiers. The required `SIM-` prefix is a `SimulationEnvironmentPolicy` for this prototype, not a `HealthcareDomainInvariant`. A hospital pilot patient-reference format is `REQUIRES_HOSPITAL_DECISION`.

If a name happens to match a real person, replace it with another synthetic name; do not use it to represent that person.

## Allowed synthetic formats

| Data type | Allowed examples | Rules |
|---|---|---|
| Employee/practitioner ID | `SIM-EMP-0101`, `SIM-PRAC-0202` | Must be synthetic and not derived from an HR/provider ID. |
| Email | `maya.chen@example.invalid` | Use reserved `.invalid` domains only. |
| Phone | `+1 555 010 0101` | Phase 0 and Development/Test fixtures use fictional `555` values only. Any future real provider test number requires `REQUIRES_HOSPITAL_DECISION` and an approved test environment; never use a personal or hospital number. |
| Patient reference | `SIM-PAT-0001` | Must visibly identify simulation. |
| Location | `North Wing / Sim Unit 2 / Room 204` | Must be fictional. |
| Age | `50–59` | Use ranges, not real dates of birth. |
| Clinical values | `HR 118 beats/min`, `RR 24 breaths/min`, `SpO2 94 %` | Values are fictional test inputs; include units and require human confirmation in workflow tests. |
| Source note | `SIMULATION: fictional note for workflow test.` | Never include copied real narrative. |
| External provider reference | `SIM-PROVIDER-MSG-0001` | Must not be a real message/call identifier. |

The example values above are not clinical guidance, thresholds, or hospital policy.

No real provider-approved test numbers, sender numbers, callback numbers, or endpoint values may be stored in this repository. A future approved test environment must keep those values outside simulation fixtures and outside source control.

## Required fixture coverage

The fictional dataset should include:

- Two fictional sites and three fictional departments, each with a synthetic `SIM-SITE-*` or `SIM-DEPT-*` code.
- At least twelve fictional practitioners.
- The Phase 4 CSV adapter fixture at `fixtures/simulation/directory-harborview.csv`.
- Similar or duplicate surnames with disambiguating specialty, department, site, role, and synthetic identifier suffixes.
- Two inactive fictional practitioners.
- Multiple fictional specialties.
- Fictional primary and backup on-call assignments.
- At least one missing optional endpoint.
- One intentionally stale directory record.
- Synthetic bilingual and code-switching source text.
- Ambiguous number, missing unit, decline, all-channel-failure, duplicate-confirmation, edit-after-review, stale-directory, and concurrent-edit scenarios.

## Prohibited data

Never add:

- Real patient names, medical record numbers, dates of birth, diagnoses, notes, images, audio, or contact details.
- Real employee or practitioner names tied to a real organization, employee IDs, schedules, credentials, or phone numbers.
- Real hospital names, logos, addresses, switchboard numbers, department codes, or on-call rosters.
- Real provider account IDs, sender numbers, callback IDs, message bodies, or webhook payloads.
- Secrets, tokens, private keys, passwords, connection strings, or credentials.
- Screenshots or logs that contain any of the above.

## Storage and sharing rules

- Every simulation UI shows `SIMULATION MODE` when synthetic data or simulated providers are active.
- Test fixtures are stored only in clearly named simulation/test locations.
- Demo data must not be used to test production configuration or real provider adapters.
- Logs and audit metadata contain identifiers and actions only; they do not duplicate clinical payloads or contact numbers.
- Synthetic source text, transcription, structured suggestions, and approved message are kept separate to exercise provenance controls.
- Raw audio is not retained by default.
- Do not paste demo payloads into public issue trackers unless they remain visibly synthetic and contain no secrets.

## Review checks before merge

Future implementation phases must add automated checks that:

- Reject non-synthetic phone and email patterns in fixtures.
- Reject missing `SIM-` markers for patient and provider references.
- Search logs, screenshots, URLs, and error payloads for forbidden sensitive fields.
- Fail closed if demo seed data is requested outside Development/Test.
- Refuse production startup when development authentication or simulation providers are enabled.
