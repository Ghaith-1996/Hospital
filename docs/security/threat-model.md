# Security Threat Model

Status: Phase 8 repository-scoped review model. The Phase 0 design baseline is extended by recipient selection, exact review, simulation-only dispatch, practitioner response, safe operator lifecycle actions, and manual-fallback display boundaries; this is not hospital approval or a production security conclusion.

## Overview

This workspace specifies a human-confirmed, closed-loop clinician alert platform for a fictional simulation. A human operator enters a source message, reviews structured suggestions, confirms critical numbers and units, manually selects recipients, confirms the exact alert version and recipients, and monitors durable delivery/response/escalation states. A worker processes an outbox and simulated providers. A future production deployment could cross hospital identity, directory, scheduling, and communications boundaries, but those integrations are not connected or authorized in Phase 0.

The highest-value security properties are:

- no dispatch without explicit human approval of the exact version and recipients;
- no autonomous clinical decision or responsibility transfer;
- strict organization/role boundaries;
- provenance and integrity of original source, suggestions, approved content, values, and units;
- confidentiality of clinical/personal content and endpoints;
- idempotent, replay-resistant delivery and callback handling;
- durable, tamper-evident-enough audit and operator-visible failures;
- fail-closed separation between simulation and production.

Relevant design anchors are [AGENTS.md](../../AGENTS.md), [workflow](../product/workflow.md), [containers](../architecture/containers.md), [data model](../architecture/data-model.md), [state machine](../architecture/alert-state-machine.md), [directory integration](../architecture/directory-integration.md), [recipient selection and review](../architecture/recipient-selection-and-review.md), [simulated dispatch](../architecture/simulated-dispatch.md), and [logging policy](logging-policy.md).

### Historical Phase 6 boundary

The current implementation permits only manual selection from the authenticated organization's fictional directory, exact-version compose/review, and authenticated idempotent confirmation. It creates an identifier-only outbox request atomically with the state transition, audit event, and idempotency record, but no worker processes the request and no provider or live dispatch surface exists. Production identity, directory, communications, retention, clinical, escalation, and policy decisions remain `REQUIRES_HOSPITAL_DECISION`.

### Historical Phase 7 boundary

The Phase 7 implementation consumes the Phase 6 outbox request only through a worker that is explicitly enabled in `Development` or `Test`. The worker claims pending or expired-lease rows with database locking, verifies the strict identifier-only payload against an organization-scoped alert and confirmed version, creates stable per-recipient/channel attempts, and uses typed in-process simulation adapters. It records normalized synthetic events with organization-scoped uniqueness, monotonic status application, bounded retries, visible terminal failure, and sanitized audit metadata. The delivery-status projection is organization-scoped and operationally safe.

No real provider SDK, network call, external callback endpoint, doctor response, live monitoring screen, or escalation decision exists in this phase. Production enablement, provider authentication/signatures, callback replay windows, delivery SLAs, retry policy, escalation policy, and operational ownership remain `REQUIRES_HOSPITAL_DECISION`.

### Current Phase 8 boundary

Phase 8 adds Development/Test-only practitioner response and operator status/lifecycle surfaces without changing the Phase 7 provider boundary. Practitioner identity comes only from the authenticated user plus an explicit organization-scoped user-to-practitioner link. Practitioner routes require the Practitioner or Physician role and return only confirmed Active alerts whose exact version addresses the linked practitioner. Operator live status and lifecycle actions require their distinct server policies and apply the authenticated organization scope; caller-supplied identity, role, organization, and practitioner values are never authoritative.

Opened, acknowledged, call-unit request, terminal disposition, responsibility assignment, delivery, and lifecycle remain separate. Idempotency records, transactions, and database uniqueness constraints protect duplicate and concurrent commands. Accepted creates one exact-version assignment; declined, unavailable, and call-unit request do not. Resolve requires an unreleased exact-version responsibility assignment; cancel requires an Active alert. The live projection excludes protected message/source/SBAR content, decrypted contact values, and raw provider references. Delivery failure remains visible and produces only a non-routing manual-fallback placeholder marked `REQUIRES_HOSPITAL_DECISION`. No external callback, real provider, automated escalation, transfer, hospital integration, or production identity is introduced.

## Threat Model, Trust Boundaries, and Assumptions

### Assets and privileges

| Asset/privilege | Why it matters |
|---|---|
| Exact approved alert version | An attacker changing it can cause a human to approve one message while the system dispatches another. |
| Recipient and channel set | An unauthorized recipient or omitted recipient can change who receives an urgent message. |
| Critical numbers and units | Silent changes can create dangerous misinformation. |
| Original source and provenance | Loss or mutation prevents review of what the human actually entered or what the provider returned. |
| Organization and role scope | Cross-organization or cross-role access could expose or alter workflow data. |
| Delivery, response, and responsibility states | Confusing delivery with acknowledgement or acceptance can create false assurance. |
| Escalation policy version and outbox | Tampering can cause duplicate, missing, premature, or stopped dispatch. |
| Directory and on-call data | Stale or mis-mapped practitioners can route alerts incorrectly. |
| Audit and logs | Investigation and accountability depend on integrity while confidentiality must be preserved. |
| Secrets and encryption keys | Compromise enables impersonation, decryption, provider abuse, or callback forgery. |

### Trust boundaries

1. **Human user ↔ web application:** operator, practitioner, and admin actions are user-controlled input and must be server-authorized.
2. **Browser ↔ API:** the browser is not trusted to enforce review, role, organization, version, recipient, or confirmation invariants.
3. **API/application ↔ PostgreSQL:** application identities and migration identities have different privileges; database constraints and transactions protect durable state.
4. **API/worker ↔ provider adapters:** provider results and callbacks are untrusted; provider acceptance is not delivery, and callback bodies cannot mutate content directly.
5. **Directory/identity/scheduling source ↔ application:** future claims, files, and records cross an integration boundary and require stable identifiers, validation, freshness, scope, and reconciliation.
6. **AI/transcription provider ↔ application:** source audio/transcript and suggestions cross a privacy and integrity boundary; output is never authoritative.
7. **Application ↔ logs/telemetry/audit:** diagnostic paths must not leak clinical payloads or secrets; audit is append-only and sanitized.
8. **Developer/CI environment ↔ repository/artifacts:** fixtures, build logs, package configuration, and deployment definitions can leak secrets or enable unsafe runtime modes.

### Attacker-controlled inputs

- Unauthenticated or low-privilege HTTP requests.
- Browser fields, headers, route IDs, query parameters, and idempotency keys.
- Authenticated but malicious operator/practitioner/admin input within their granted scope.
- Provider webhook headers, event IDs, status values, timestamps, and bodies.
- Uploaded directory CSV rows and future directory/scheduling payloads.
- Transcription/audio-derived text and future AI suggestion output.
- Network failures, duplicate messages, out-of-order events, time skew, and worker restarts.
- Dependency/package/build inputs and CI configuration changes.

### Operator-controlled and developer-controlled inputs

Operators control source text, human approvals, recipient selection, and response actions but must not be able to bypass server invariants. Developers control code, migrations, fixtures, configuration defaults, and CI; a developer mistake can create a safety or privacy failure even without an external attacker. Simulation configuration must fail closed outside Development/Test.

### Assumptions and exclusions

- Phase 8 verification is limited to the repository tests and checks reported at the review gate; passing simulation tests is not evidence of production suitability.
- Simulation data and providers are fictional and local; no real hospital network or provider is trusted or connected.
- Production identity, privacy, data residency, retention, directory, scheduling, communication, and clinical workflow decisions are `REQUIRES_HOSPITAL_DECISION`.
- The system is not an EHR, clinical decision support tool, medical device integration, or replacement for a hospital's approved fallback.
- A hospital-approved identity provider, network, endpoint, and policy may be trusted only after validation and contract/security review.

## Attack Surface, Mitigations, and Attacker Stories

The controls below are planned repository-wide controls, not current findings.

| Surface | Relevant classes | Required mitigations | Example attacker story |
|---|---|---|---|
| Alert create/edit/confirm API | Authorization bypass, IDOR, race conditions, parameter tampering, replay | Server-side organization/role checks; exact-version optimistic concurrency; required field/unit confirmations; recipient and channel validation; idempotency; atomic outbox transaction; safe problem details. | A user changes a draft after the operator reviewed it, then submits the old confirmation. The server must reject stale/mismatched versions. |
| Recipient search and directory | Cross-organization access, name collision, stale data, unauthorized recipient injection | Scope every query; stable source IDs; display disambiguators; inactive/stale states; manual selection only; no hidden expansion; reconcile imports with conflict report. | A malicious row or background sync adds a look-alike practitioner to an approved alert. The system must not add recipients invisibly. |
| Browser/UI | XSS, CSRF, click confusion, unsafe defaults, data leakage | Server remains authoritative; output encoding; CSRF protections where applicable; deliberate confirmation language; keyboard/accessibility checks; no sensitive payload in URLs; simulation banner. | A crafted source value changes the meaning of the review page or tricks an operator into confirming a different recipient set. |
| Provider dispatch and callbacks | Webhook forgery, replay, duplicate delivery, out-of-order regression, SSRF/provider abuse | Signed/authenticated callbacks; timestamp/replay checks; inbox uniqueness; normalized allowlisted statuses; idempotency; provider abstraction; no provider-controlled clinical content. | An attacker replays a “delivered” callback or submits a forged failure to alter escalation. The system must authenticate, deduplicate, and preserve monotonic durable semantics. |
| Practitioner response and operator status | Forged practitioner identity, IDOR, cross-organization access, replay, conflicting terminal responses, protected-value disclosure | Explicit server-resolved organization/user/practitioner link; exact addressed-version checks; role policies; idempotency; transactional uniqueness; non-disclosing not-found responses; allowlisted status DTOs. | A caller submits another practitioner's ID or organization and attempts to accept an alert. The server must ignore caller identity fields, resolve the authenticated link, and reject unaddressed or foreign alerts without disclosure. |
| Worker/outbox/escalation | Duplicate sends, lost work, lease races, policy tampering, denial of service | Transactional outbox; lease ownership/expiry; stable per-attempt keys; bounded retries; dead-letter/visible failure; approved policy version; database time; concurrency tests. | Two workers lease the same outbox row and send twice. The database and provider idempotency key must prevent duplicate delivery. |
| AI/transcription boundary | Prompt injection, unsupported inference, provenance loss, sensitive-data leakage | Preserve exact source; store suggestion separately; evidence spans/confidence; unresolved values remain unresolved; no direct mutation/dispatch; provider/retention approval; redaction. | A transcript contains instructions such as “send now to Dr. X.” The text remains source content; it cannot select a recipient or dispatch. |
| Identity/session/admin | Account takeover, privilege escalation, unsafe dev auth, deprovisioning gap | Fixed seeded users only in Development/Test; startup failure for dev auth elsewhere; approved SSO/MFA; tenant and scope checks; short-lived sessions; lifecycle tests; no public signup. | A request header claims to be an admin or a deactivated user. The server must reject it outside the fixed simulation handler and honor real lifecycle controls in production. |
| Logs/audit/telemetry | PHI/PII leakage, secret leakage, audit tampering, correlation abuse | Allowlisted fields; redaction tests; no bodies/endpoints/tokens; append-only audit; safe errors; access and retention approval. | An exception serializes the alert body into centralized logs. Logging tests and safe exception handling must prevent this. |
| Configuration/CI/secrets | Secret exposure, unsafe environment promotion, dependency compromise | Ignore local env files; secret scanning; pinned dependencies; production refuses simulation/dev auth; managed secret store; separate migration identity; review CI artifacts. | A demo connection string or provider token is committed or a production build enables simulated auth. Checks must fail closed. |

### High-value invariants to preserve

- The message approved by the human is byte/field-equivalent to the message version authorized for dispatch.
- Recipient set and channel set cannot change between review, confirmation, and outbox persistence.
- No AI or provider output can mutate approved content or add a recipient.
- Critical values and units are explicit, versioned, and individually confirmed.
- Delivery/opened/acknowledged/responsibility states cannot be collapsed or falsely advanced.
- Channel capability must distinguish `NotApplicable` from `Pending/NotObserved` for delivery states.
- Replayed/out-of-order external events cannot cause duplicate sends or unsafe state regression.
- Organization and role scope applies to every read, write, callback, worker job, and audit query.
- Simulation configuration cannot activate in staging/production.

### Out-of-scope attacker stories

Real hospital network compromise, real provider account takeover, insider clinical misconduct, physical device theft, and legal/regulatory determinations are not solved by this Phase 0 document. They remain risks for hospital security, identity, vendor, privacy, and operational workstreams and require `REQUIRES_HOSPITAL_DECISION`. They must not be treated as evidence that the application controls are sufficient.

## Severity Calibration (Critical, High, Medium, Low)

Severity is based on realistic reachability, affected trust boundary, confidentiality/integrity/availability impact, and whether the issue can create false clinical responsibility. These examples are calibration guidance, not findings against current code.

### Critical

Use Critical for a broadly reachable or low-friction issue that can bypass human confirmation or cross major organization boundaries with material content/control impact.

Examples:

- An unauthenticated or ordinary user can cause an alert to dispatch without exact human confirmation, alter the approved message, or add arbitrary recipients.
- A cross-organization authorization failure exposes or changes real clinical content, identity/contact data, or responsibility state at scale.
- Production secrets/keys are exposed in a public artifact and enable impersonation, decryption, or mass provider dispatch.

### High

Use High for a strongly exploitable issue with material impact within an organization or on a major workflow boundary, even if additional authentication or scope is required.

Examples:

- Forged/replayed webhooks or worker races create duplicate urgent calls/messages, falsely mark a recipient as reached, or stop required escalation.
- A privileged but not fully administrative user can read protected alert content, endpoint data, or audit history outside their approved scope.
- Stale/deactivated directory data can be selected without warning in a way that systematically routes alerts to the wrong person.

### Medium

Use Medium for limited-scope confidentiality, integrity, or availability issues that require a narrower precondition or do not directly bypass the central confirmation invariant.

Examples:

- An authenticated user can view another in-scope department's nonclinical alert metadata because a query filter is incomplete.
- A stored XSS issue affects an authenticated review page but server-side confirmation still validates the exact approved version and scope.
- Provider outage or retry handling creates a durable delay or visible failure without silent loss, but exceeds approved operational limits.

### Low

Use Low for defense-in-depth weaknesses with limited impact in the intended deployment, especially where synthetic simulation data is the only reachable data.

Examples:

- A development-only diagnostic reveals a synthetic provider reference but no secret, payload, or endpoint value.
- A non-sensitive status endpoint lacks a tighter rate limit while it exposes no protected state.
- A UI warning is visually ambiguous but the server still blocks unsafe confirmation and provides an accessible text error.

Severity must be reassessed when real data, identity, hospital integrations, or production communications are introduced. The hospital security owner must approve the final risk treatment.

Repository: local-workspace:Hospital
Version: codex-security-snapshot/v1:sha256:25c00f63c5b43fe3578885c04c824290231ae71d8b3290a110d337333bf49d50
