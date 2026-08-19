# Data Classification

Status: Phase 0 handling baseline. It is not a legal classification or privacy-impact assessment.

## Classification categories

| Class | Meaning in this project | Simulation rule | Production status |
|---|---|---|---|
| Synthetic simulation data | Deliberately fictional organization, person, patient reference, endpoint, schedule, and clinical values. | Allowed only in Development/Test and clearly labelled `SIMULATION MODE`. | Must not be mixed with real data. |
| Operational metadata | Service name, environment, non-sensitive status, correlation ID, safe error category, and synthetic resource identifier. | May appear in PHI-safe logs when it cannot reconstruct clinical content. | Retention and access are `REQUIRES_HOSPITAL_DECISION`. |
| Protected workflow content | Original source, transcript, structured suggestion, approved message, critical values/units, patient reference, and responsibility notes. | Use synthetic values only; keep out of logs, URLs, analytics, and provider wake-up payloads. | Permitted fields, encryption, access, residency, retention, export, and deletion are `REQUIRES_HOSPITAL_DECISION`. |
| Personal/clinical data | Any real patient, employee, practitioner, health, schedule, contact, or identity data. | Prohibited in all Phase 0/Development/Test artifacts. | `REQUIRES_HOSPITAL_DECISION` plus privacy/legal/security approval before use. |
| Secret/credential | Passwords, API keys, certificates, private keys, tokens, connection strings, signing keys, and encryption keys. | Never commit, log, screenshot, or place in fixtures. | `REQUIRES_HOSPITAL_DECISION` for custody, rotation, and access model. |

## Handling rules

- Do not attempt to de-identify real records for simulation; generate synthetic records instead.
- Keep original source, transcription, suggestions, and approved content separate so provenance is preserved.
- Protect sensitive fields behind a dedicated data-protection interface. Never put a decryption key in frontend code.
- Store identifiers in URLs only when they are non-sensitive opaque resource IDs and the hospital approves that design; never put clinical payloads, patient identifiers, contact numbers, tokens, or credentials in URLs.
- SMS and voicemail receive generic wake-up content only by default.
- Audit records contain action, actor, resource, outcome, correlation ID, and sanitized metadata; they do not duplicate full clinical payloads.
- Retention, deletion, legal hold, export, access review, and residency are `REQUIRES_HOSPITAL_DECISION`.

## Phase gates

Before a real data pilot, obtain a hospital-approved classification and privacy impact assessment. Before production, confirm encryption/key management, data flows, subprocessors, region, retention, deletion, incident response, and access review in writing.

