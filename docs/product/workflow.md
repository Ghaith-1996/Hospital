# Workflow Specification

Status: Proposed simulation workflow with the Phase 6 review path implemented locally. It is not a hospital-approved clinical workflow or escalation policy.

Phase 6 stops after authenticated confirmation creates an identifier-only `AlertDispatchRequested` outbox item in the same transaction as the state, audit, and idempotency records. It does not process that item, create delivery attempts, call providers, retry, escalate, receive callbacks, or expose a live screen. Those behaviors remain later-phase work and production choices remain `REQUIRES_HOSPITAL_DECISION`.

## Workflow identity

- Identifier: `SIM-URGENT-CONSULT-001`.
- Name: fictional urgent specialist consultation.
- Mode: simulation only.
- Human operator: seeded fictional `Operator` user.
- Final recipient: selected manually by the human operator.
- AI role: none required for Phase 0; future transcription and formatting suggestions remain separate from source and approved content.

The production workflow owner, authorized roles, clinical trigger, required data, urgency vocabulary, escalation policy, fallback, and resolution criteria are `REQUIRES_HOSPITAL_DECISION`.

## Safety invariants

1. A human operator creates or edits the source content.
2. Original typed or transcribed content is immutable history and is never replaced by a structured suggestion.
3. Structured fields and AI suggestions are separate from the original source and from approved content.
4. Every critical number and unit is individually confirmed by a human before dispatch.
5. Recipients are manually selected. A specialty suggestion may never select or pre-check a practitioner.
6. The review screen shows the exact alert version, message, critical values and units, recipient list, channel list, and escalation-policy version.
7. Dispatch requires an explicit authenticated human confirmation of that exact version and recipient set.
8. Any edit to content, critical values, units, urgency, policy version, or recipients invalidates confirmation.
9. A provider's accepted/submitted status is not delivery; delivery is not opening; opening is not acknowledgement; acknowledgement is not responsibility acceptance.
10. Only an approved human workflow may stop escalation. AI and background workers may not decide to stop escalation.
11. Failed delivery and provider outage remain visible and actionable.
12. No SMS or voicemail contains patient, clinical, or detailed case content by default.
13. A channel state that is not supported is recorded as `NotApplicable`, not as pending, failed, or successful.

## Simulation flow

```mermaid
flowchart TD
    A[Seeded fictional operator starts draft] --> B[Enter fictional source text]
    B --> C[Review original source separately]
    C --> D[Review structured fields or suggestions]
    D --> E[Confirm every critical number and unit]
    E --> F[Manually search and select practitioners]
    F --> G[Review exact version, message, recipients, channels, policy reference]
    G --> H{Explicit human confirmation?}
    H -- No --> I[Remain pending confirmation]
    H -- Yes --> J[Persist approval and dispatch request atomically]
    J --> K[Simulated channels create separate delivery states]
    K --> L[Recipient may open and acknowledge]
    L --> M[Recipient may accept, decline, or be unavailable]
    M --> N[Versioned DEMO policy evaluates next step]
    N --> O[Human records resolution or approved fallback]
```

## Step-by-step behavior

### 1. Start a draft

The fictional operator starts a draft. The system records the creator and organization scope. The system does not infer urgency or choose a recipient.

The production authorization rule is `REQUIRES_HOSPITAL_DECISION`; the simulation uses only a fixed Development/Test identity.

### 2. Enter source content

The operator types a fictional note. A future transcription adapter may provide an exact transcript, but the transcript is stored as a separate source representation. Every source revision is preserved as immutable history; editing creates a new draft version rather than overwriting the prior source. Raw audio is not retained by default.

The source remains available for review and audit references without putting the clinical payload into ordinary logs.

### 3. Review structured content

Structured SBAR-style fields, missing-field notices, uncertainty markers, and any future AI output are suggestions. They cannot mutate the original source or approved alert directly. The operator must approve the content that will be dispatched.

Ambiguous abbreviations, numbers, units, laterality, dates, times, negation, and medication-like terms remain unresolved until a human confirms them. Missing data remains missing.

### 4. Confirm critical values

Each extracted number is displayed with its unit, source/evidence reference when available, and confirmation state. A draft cannot be dispatched while any required number or unit is unresolved. The exact required set for a real hospital template is `REQUIRES_HOSPITAL_DECISION`.

### 5. Select recipients manually

The operator searches the fictional directory by name, specialty, department, site, and on-call display. Similar names show disambiguating fields. Inactive or stale entries are visibly flagged and selection behavior for a real hospital is `REQUIRES_HOSPITAL_DECISION`.

No background process may add a recipient. The system records who selected each recipient and which directory timestamp was shown.

### 6. Review and confirm

The review screen must display:

- Exact alert draft version.
- Operator-selected urgency, without AI classification.
- Fictional patient reference and location.
- Exact approved message.
- Every critical number and unit with confirmation state.
- Every recipient, role/specialty/site disambiguator, and selected channel.
- Directory freshness timestamp/source.
- Escalation policy/version reference, clearly labelled `DEMO` in simulation.

The operator explicitly confirms the exact version and recipient set. The confirmation operation is idempotent. A second submission of the same idempotency key produces no duplicate dispatch request.

### 7. Queue dispatch

The confirmation transaction persists the approved version, recipients, state transition, audit event, and `AlertDispatchRequested` outbox message together. No provider is called before the approved version is durable.

### 8. Deliver and track

The simulation worker creates per-recipient, per-channel delivery attempts with stable idempotency keys. It records these dimensions independently:

- Provider request/submission.
- Delivery outcome.
- Opened outcome when the channel supports an authenticated view; otherwise `NotApplicable`.
- Recipient acknowledgement.
- Responsibility acceptance.

These are not a single linear clinical state. A supported state may be pending/not observed, occurred, failed, or not applicable according to the channel capability. A recipient can acknowledge without accepting responsibility. A provider failure cannot be hidden by a later success on another channel.

### 9. Respond and escalate

Fictional recipients may acknowledge, accept, decline, or mark unavailable. The system records the actor, time, response type, and sanitized reason code. Escalation is deterministic and tied to the policy version captured at confirmation.

The exact production trigger, delay, retry count, stop condition, backup hierarchy, and override authority are `REQUIRES_HOSPITAL_DECISION`. Simulation timing is labelled `DEMO` and uses a fake clock.

### 10. Resolve, cancel, or use fallback

The final action is an explicit human action. The exact roles and clinical criteria are `REQUIRES_HOSPITAL_DECISION`. The simulation can exercise resolve, cancel, and visible manual-fallback states without asserting that any of them is appropriate in a hospital.

## Exception paths

| Scenario | Required behavior |
|---|---|
| Missing required field | Block confirmation; show the missing field; do not invent a value. Required production fields are `REQUIRES_HOSPITAL_DECISION`. |
| Ambiguous number or missing unit | Block confirmation until the human resolves and confirms the exact number and unit. |
| Operator edits after review | Increment the draft version and invalidate approval; require a fresh review and confirmation. |
| Recipient changes after review | Invalidate approval and require a fresh review and confirmation. |
| Similar practitioner names | Display disambiguating attributes; never select by name alone. |
| Stale/inactive directory entry | Show freshness; block or permit selection only according to `REQUIRES_HOSPITAL_DECISION`; simulation may block it. |
| Duplicate confirmation | Idempotency and optimistic concurrency prevent duplicate dispatch. |
| Provider callback replay/out of order | Authenticate, validate, deduplicate, and normalize without regressing durable state. |
| All channels fail | Show a durable operator-visible failure and the hospital-approved fallback placeholder; never silently disappear. |
| Acknowledged but not accepted | Keep acknowledgement and responsibility separate; escalation follows the approved policy rather than stopping automatically. |
| Channel cannot report opened | Record `NotApplicable`; do not infer opened, acknowledged, or responsibility accepted. |
| Concurrent operator edit | Reject stale version updates and require the operator to refresh/review. |

## Production decision register

The following are intentionally unresolved and must not be inferred from this simulation:

- `REQUIRES_HOSPITAL_DECISION`: authorized starter roles and scope.
- `REQUIRES_HOSPITAL_DECISION`: clinical trigger and urgency vocabulary.
- `REQUIRES_HOSPITAL_DECISION`: template fields, terminology, abbreviations, and numeric-field rules.
- `REQUIRES_HOSPITAL_DECISION`: recipient eligibility, off-call selection, backup hierarchy, and on-call source.
- `REQUIRES_HOSPITAL_DECISION`: acknowledgement, responsibility acceptance, resolution, cancellation, and transfer semantics.
- `REQUIRES_HOSPITAL_DECISION`: escalation delays, retries, stop conditions, overrides, and manual fallback.
- `REQUIRES_HOSPITAL_DECISION`: permitted patient information, retention, access, audit review, and deletion.
- `REQUIRES_HOSPITAL_DECISION`: approved identity, directory, scheduling, communications, and integration sources.
