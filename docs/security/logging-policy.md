# Logging Policy

Status: Phase 6 PHI-safe logging control. This policy is a design control, not a hospital-approved retention schedule.

## Purpose

Logs should explain service health, security events, workflow transitions, and failure categories without duplicating clinical content or contact data. Audit events answer who did what to which resource and when; application logs answer how the system behaved.

## Prohibited fields

Never write these values to application logs, exception messages, metrics labels, traces, analytics, URLs, browser history, screenshots, or provider diagnostics:

- Original typed content or transcripts.
- Structured clinical suggestions or approved message bodies.
- Patient names, references, medical record numbers, dates of birth, diagnoses, vitals, or clinical notes.
- Phone numbers, email addresses, pager addresses, endpoint values, or voicemail content.
- Access tokens, refresh tokens, API keys, passwords, private keys, connection strings, cookies, or webhook secrets.
- Raw audio or audio-derived payloads.
- Full provider webhook bodies.
- Secrets supplied in request headers or query strings.

## Allowed structured fields

Subject to hospital approval, structured logs may include:

- UTC timestamp.
- Log level.
- Service/process name and version.
- Environment (`Development`, `Test`, `Staging`, or `Production`).
- Correlation ID and operation name.
- Safe result/outcome code.
- Duration and retry count.
- Opaque internal resource ID, synthetic simulation ID, or hashed provider reference where operationally necessary.
- Exception type and a safe error category, never an exception message containing payload data.
- Organization and actor identifiers only when access and retention are approved; avoid display names.

IP/device metadata is `REQUIRES_HOSPITAL_DECISION` and must not be added by assumption.

## Audit events

Audit events are append-only and record:

- organization scope;
- actor type and opaque actor ID;
- action/resource type/resource ID;
- outcome;
- correlation ID;
- timestamp;
- sanitized reason or metadata.

Audit events must identify confirmation, edit/reconfirmation, recipient selection, dispatch request, provider event handling, response, escalation, resolution, cancellation, access, and administrative changes without copying the clinical body. The exact audit review audience, retention, export, and legal hold are `REQUIRES_HOSPITAL_DECISION`.

## Boundary-specific rules

### API and UI

- Redact request bodies by default for alert, transcription, response, and webhook endpoints.
- Do not log query strings for sensitive routes.
- Return RFC 7807 problem details with safe public error text and a correlation ID.
- Do not reflect untrusted input into logs without structured encoding.

### Worker and providers

- Log identifiers, channel type, attempt number, status category, and timing—not endpoint values or message bodies.
- Store provider callback IDs only where needed for idempotency, preferably as protected/hashed references.
- Record duplicate, out-of-order, rejected-signature, and rate-limit outcomes as safe categories.

### AI and transcription

- Do not log prompts, transcripts, source audio, model output, evidence spans, or clinical payloads.
- Log provider/version/configuration identifiers and aggregate evaluation results only when they contain no sensitive text.
- Retain raw audio only if a separately approved policy requires it; the default is no retention.

## Redaction and verification

- Use allowlisted structured fields rather than broad request logging.
- Apply redaction at the logging boundary and test it with synthetic payloads containing sentinel values.
- Add automated checks that fail if known synthetic message bodies, patient references, phone numbers, tokens, or secret patterns appear in logs or errors.
- Verify that correlation IDs cannot be used to retrieve protected content without authorization.

## Phase 6 boundary

Phase 6 confirmation audit metadata is limited to actor and organization identifiers, alert and version identifiers, action/outcome, UTC time, recipient count, channel kinds, `DEMO` policy version identifiers, and correlation ID. The identifier-only outbox item contains only the alert identifier and draft version. Source text, SBAR, approved message, patient content, practitioner names, contact values, and complete request bodies remain excluded from logs, audit metadata, idempotency records, and outbox payloads.

## Access, retention, and incident response

Log access, centralized storage, cross-border transfer, retention, deletion, legal hold, SIEM integration, alert thresholds, and incident-response ownership are `REQUIRES_HOSPITAL_DECISION`. Until approved, keep simulation logs local, minimize retention, and do not send them to external services.

