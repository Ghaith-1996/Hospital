# Product Decisions

Status: Phase 0 decision register. This is a template and simulation baseline, not a production hospital policy.

## How to use this document

The project owner must record a decision, decision owner, evidence, and approval date before a value can govern a real hospital workflow. Until then, use `REQUIRES_HOSPITAL_DECISION` exactly as written. A simulation value is valid only inside an explicitly labelled `SIMULATION_ONLY_ASSUMPTION` section and must never become a production default.

The user's mandatory rules are binding. The attached master build plan supplies the baseline scope and recommended sequencing, but does not approve any hospital workflow, escalation period, privacy treatment, security control, or integration.

## Product identity

| Decision | Current value | Authority needed | Notes |
|---|---|---|---|
| Product working name | `REQUIRES_PROJECT_OWNER_DECISION` | Project owner | The repository folder is not a product-name decision. |
| Company/project owner | `REQUIRES_PROJECT_OWNER_DECISION` | Project owner | Must be identified before external coordination. |
| Initial jurisdiction | `REQUIRES_HOSPITAL_DECISION` | Hospital/legal/privacy stakeholders | The master plan proposes Québec/Canada first; that is not adopted as a production decision. |
| Initial language(s) | `REQUIRES_HOSPITAL_DECISION` | Hospital workflow owner | Simulation data may include English and French examples only as test fixtures. |
| Intended buyer/operator | `REQUIRES_HOSPITAL_DECISION` | Sponsor and workflow owner | Hospital, department, switchboard, or health network is unresolved. |
| Hosting model | `REQUIRES_HOSPITAL_DECISION` | Hospital IT/security/privacy stakeholders | Vendor cloud, hospital cloud, and on-premises have different obligations. |
| Intended use statement | `REQUIRES_HOSPITAL_DECISION` | Clinical safety, legal, privacy, and product owners | No clinical responsibility may be implied by the product. |

## Proposed first workflow

`SIMULATION_ONLY_ASSUMPTION`: use the neutral scenario “urgent specialist consultation initiated by a fictional authorized operator, with manually selected fictional clinicians and documented responsibility acceptance.” This is a testable simulation workflow, not an approved pilot workflow.

| Workflow decision | Production value | Simulation treatment |
|---|---|---|
| Who may start an alert | `REQUIRES_HOSPITAL_DECISION` | A seeded fictional user with the simulation `Operator` role. |
| Event that triggers the workflow | `REQUIRES_HOSPITAL_DECISION` | A manually entered fictional urgent consultation scenario. The system does not infer urgency. |
| Required fields | `REQUIRES_HOSPITAL_DECISION` | Source text, operator-selected urgency field, fictional patient reference, fictional location, approved message, at least one manually selected recipient, and confirmed critical numbers/units when present. |
| Who may receive | `REQUIRES_HOSPITAL_DECISION` | Only manually selected active fictional practitioners in the seeded directory. |
| Must all recipients respond? | `REQUIRES_HOSPITAL_DECISION` | The simulation displays every recipient separately and allows each to acknowledge, accept, decline, or be unavailable. It does not imply a production quorum. |
| Acknowledgement meaning | `REQUIRES_HOSPITAL_DECISION` | A fictional recipient records that the alert was opened/seen in the simulation. This does not accept responsibility. |
| Responsibility acceptance meaning | `REQUIRES_HOSPITAL_DECISION` | A fictional practitioner deliberately records acceptance in the simulation. This is separate from acknowledgement. |
| Resolution meaning | `REQUIRES_HOSPITAL_DECISION` | A fictional authorized operator performs an explicit resolve action after the test scenario's acceptance condition. The production condition is not defined. |
| Who may cancel | `REQUIRES_HOSPITAL_DECISION` | The simulation exposes cancel only to seeded roles configured for the fixture; no production role is inferred. |
| No-response behavior | `REQUIRES_HOSPITAL_DECISION` | The simulation records a deterministic, versioned `DEMO` policy chosen for tests. It must not be reused in production. |
| Manual fallback | `REQUIRES_HOSPITAL_DECISION` | The UI must show a placeholder instruction that a hospital-approved manual fallback is required; no real number or procedure is invented. |
| Escalation policy source | `REQUIRES_HOSPITAL_DECISION` | A versioned simulation policy is referenced by identifier only. The worker must not create or infer production policy. |

## Allowed patient information

| Field | Simulation disposition | Production disposition |
|---|---|---|
| Internal patient reference | Synthetic `SIM-PAT-*` value only | `REQUIRES_HOSPITAL_DECISION` |
| Medical record number suffix | Not used by default | `REQUIRES_HOSPITAL_DECISION` |
| Initials | Synthetic fixture only if needed | `REQUIRES_HOSPITAL_DECISION` |
| Age range | Synthetic fixture only | `REQUIRES_HOSPITAL_DECISION` |
| Unit/room | Fictional location only | `REQUIRES_HOSPITAL_DECISION` |
| Full name | Prohibited in simulation | `REQUIRES_HOSPITAL_DECISION` |
| Date of birth | Prohibited in simulation | `REQUIRES_HOSPITAL_DECISION` |
| Diagnosis | Prohibited in simulation | `REQUIRES_HOSPITAL_DECISION` |
| Vitals | Fictional values only, with explicit unit confirmation | `REQUIRES_HOSPITAL_DECISION` |
| Free-text note | Synthetic text only; original and structured forms remain separate | `REQUIRES_HOSPITAL_DECISION` |

## Alert template decisions

| Decision | Current value | Authority needed |
|---|---|---|
| Template name/version | `REQUIRES_HOSPITAL_DECISION` | Hospital workflow owner |
| Required/optional fields | `REQUIRES_HOSPITAL_DECISION` | Clinical workflow and patient-safety owners |
| Allowed urgency labels | `REQUIRES_HOSPITAL_DECISION` | Hospital workflow and clinical-safety owners |
| Approved terminology and abbreviations | `REQUIRES_HOSPITAL_DECISION` | Hospital clinical owner |
| Numeric fields requiring confirmation | `REQUIRES_HOSPITAL_DECISION` | Hospital clinical owner |
| Supported languages | `REQUIRES_HOSPITAL_DECISION` | Hospital workflow owner |
| Message length limits | `REQUIRES_HOSPITAL_DECISION` | Hospital communications/privacy owner and provider |
| SMS/voicemail wording | `REQUIRES_HOSPITAL_DECISION` | Hospital communications/privacy/security owners |
| Disclaimers | `REQUIRES_HOSPITAL_DECISION` | Hospital legal/privacy/clinical owners |

## Recipient and escalation decisions

| Decision | Current value |
|---|---|
| Selection unit: individual, team, role, or schedule | `REQUIRES_HOSPITAL_DECISION` |
| Whether off-call practitioners can be selected | `REQUIRES_HOSPITAL_DECISION` |
| Whether backups may be preconfigured | `REQUIRES_HOSPITAL_DECISION` |
| Roles allowed to override escalation | `REQUIRES_HOSPITAL_DECISION` |
| Whether received and accepted are distinct | `REQUIRES_HOSPITAL_DECISION`; the simulation keeps them distinct to honor the safety rule |
| Decline behavior | `REQUIRES_HOSPITAL_DECISION` |
| Whether acknowledgement stops escalation | `REQUIRES_HOSPITAL_DECISION`; the simulation does not stop on acknowledgement alone |
| Responsibility transfer record | `REQUIRES_HOSPITAL_DECISION` |
| Escalation delays, retry limits, and stop conditions | `REQUIRES_HOSPITAL_DECISION`; no production timing is invented |

## Human roles required before a real pilot

Each role needs a named person and approval evidence. The current value for every role is `REQUIRES_HOSPITAL_DECISION`:

- Product owner.
- Clinical workflow owner.
- Patient-safety/clinical-risk owner.
- Privacy officer or privacy counsel.
- Security lead.
- Hospital IT integration lead.
- Hospital directory/identity owner.
- Scheduling-system owner.
- Incident-response owner.
- Support/on-call owner.
- Procurement/legal contact.
- Pilot department champion.

## Simulation-only assumptions

These assumptions are intentionally narrow and cannot load into production:

- The organization, sites, departments, users, practitioners, patients, phone numbers, and clinical values are fictional.
- Authentication uses fixed seeded identities only in Development/Test.
- Notification channels are simulated; no external SMS, voicemail, voice, pager, or secure-message provider is contacted.
- Directory data comes from a fictional CSV fixture with stable synthetic identifiers.
- A deterministic fake clock may be used for tests. Any fast escalation timing is labelled `DEMO` and is not a hospital policy.
- The application displays `SIMULATION MODE` whenever simulation data or providers are enabled.
- No AI feature is required for the workflow. Any future transcription or structuring output is a suggestion, never approved content.

## Deterministic simulation contract

The following is a `SIMULATION_ONLY_ASSUMPTION` contract for repeatable tests. It is not a hospital policy and must not be enabled in production.

| Fictional simulation role | Allowed simulation actions |
|---|---|
| `SIMULATION_OPERATOR` | Create/edit drafts, confirm numbers and units, manually select recipients, confirm dispatch, monitor status, and invoke fixture-approved operator actions. |
| `SIMULATION_PRACTITIONER` | View alerts addressed to that fictional practitioner and record acknowledgement, responsibility acceptance, decline, or unavailability. |
| `SIMULATION_DIRECTORY_ADMIN` | Preview/import the fictional directory CSV and review freshness/conflict results. |
| `SIMULATION_AUDITOR` | Read sanitized simulation audit events without changing workflow state. |

The simulation contract also fixes these non-clinical test mechanics:

- Operator-selected urgency is represented only by a visibly `DEMO` label; AI never assigns it.
- Cancellation is available only before `DispatchQueued` in simulation tests.
- Resolution is available only after the simulation records at least one responsibility-accepted response; this does not represent clinical resolution.
- Acknowledgement and responsibility acceptance are separate actions and separate records.
- A deterministic fake clock may drive a versioned `DEMO` policy. No production delay, retry count, stop condition, or backup rule is defined here.
- Every source edit creates a new draft version while preserving the previous typed/transcribed source revision as immutable history.

If a test needs behavior outside this contract, it must add another explicitly labelled simulation assumption rather than invent a production rule.

## Approval record

| Review item | Decision | Owner | Evidence/date |
|---|---|---|---|
| Simulation scope | `REQUIRES_PROJECT_OWNER_DECISION` | Project owner |  |
| First real/pilot workflow | `REQUIRES_HOSPITAL_DECISION` | Hospital workflow owner |  |
| Privacy and permitted data | `REQUIRES_HOSPITAL_DECISION` | Privacy/legal owner |  |
| Security and identity | `REQUIRES_HOSPITAL_DECISION` | Security/IT owner |  |
| Directory and on-call source | `REQUIRES_HOSPITAL_DECISION` | Directory/scheduling owner |  |
| Communication wording/providers | `REQUIRES_HOSPITAL_DECISION` | Communications/privacy owner |  |
| Phase 0 approval | `REQUIRES_PROJECT_OWNER_DECISION` | Project owner |  |
