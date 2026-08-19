# Production Readiness Gates

Status: Not approved. Every unchecked item requires evidence and named human approval before real hospital use.

This checklist prevents a simulation baseline from being mistaken for production authorization. The project must not launch because software tests pass alone.

## Intended use and workflow

- [ ] Product intended use is written and legally/clinically reviewed: `REQUIRES_HOSPITAL_DECISION`.
- [ ] A named hospital sponsor and pilot department approve the exact workflow.
- [ ] Authorized starter roles and organization/site/department scope are approved.
- [ ] Required template fields, urgency vocabulary, approved terminology, abbreviations, languages, and critical-number rules are approved.
- [ ] Acknowledgement, responsibility acceptance, resolution, cancellation, transfer, decline, unavailable, and no-response meanings are approved.
- [ ] Escalation triggers, delays, retries, stop conditions, backup hierarchy, and overrides are approved; no demo timing is promoted.
- [ ] Manual fallback and downtime procedures are approved, trained, and rehearsed.

## Human confirmation and safety

- [ ] Server-side authorization prevents dispatch without explicit human confirmation of the exact alert version and recipients.
- [ ] Any edit invalidates prior confirmation and requires reconfirmation.
- [ ] Critical numbers and units cannot bypass explicit confirmation.
- [ ] Original typed/transcribed content, structured suggestions, and approved content are separate and auditable.
- [ ] AI cannot diagnose, assign urgency, select final recipients, stop escalation, or dispatch autonomously.
- [ ] Delivered, opened, acknowledged, and responsibility accepted are visibly distinct.
- [ ] Human factors/accessibility review confirms that confirmation consequences are clear under the intended workflow.

## Privacy and legal

- [ ] Privacy impact assessment is approved.
- [ ] Intended legal/service-provider role is documented.
- [ ] Data-processing, vendor, subprocessor, and insurance agreements are complete.
- [ ] Permitted patient and employee fields are approved.
- [ ] Data residency and cross-border processing are approved.
- [ ] Retention, deletion, legal hold, export, access review, and breach obligations are approved.
- [ ] Marketing and product claims do not imply autonomous clinical decision-making or responsibility.

## Security and identity

- [ ] Threat model is reviewed and accepted by the security owner.
- [ ] SSO/tenant restriction/MFA/session lifecycle are validated.
- [ ] Deprovisioning and directory lifecycle tests pass.
- [ ] Development authentication and simulation providers cannot run in Staging/Production.
- [ ] Organization and role authorization matrix is tested, including cross-organization denial.
- [ ] Secrets come from an approved secret store/managed identity; no secrets are in Git or production `.env` files.
- [ ] Encryption, key custody, rotation, backup protection, and recovery are tested.
- [ ] Webhook signatures, replay prevention, rate limits, request limits, CSRF/XSS/SSRF controls, and dependency scanning are complete.
- [ ] Penetration test and critical remediation are complete.
- [ ] Vulnerability disclosure and incident-response contacts are assigned.

## Directory and scheduling

- [ ] System-of-record owners, stable identifiers, mapping, deactivation, freshness, conflict, and support rules are documented.
- [ ] On-call source, update frequency, backup hierarchy, and emergency behavior are approved.
- [ ] Stale/inactive practitioner behavior is approved and tested.
- [ ] Sandbox, authentication, network allowlists, data dictionary, and de-identified sample payloads are available.
- [ ] No practitioner is matched solely by name.

## Communications

- [ ] Provider, contract, region, sender identity, endpoint ownership, throughput, and webhook terms are approved.
- [ ] SMS and voicemail wording is approved and contains generic wake-up content only by default.
- [ ] Provider accepted/submitted, delivered, opened, acknowledged, accepted, failed, and callback semantics are mapped and tested.
- [ ] Provider outage, retry, duplicate, out-of-order, and all-channel-failure behavior is tested.
- [ ] If real provider testing is proposed, the provider-approved test numbers, sender identity, contracts, and test environment are documented under `REQUIRES_HOSPITAL_DECISION`; otherwise testing remains fictional `555` simulation-only data.

## Reliability and operations

- [ ] PostgreSQL migrations run through a controlled job with a migration-specific identity.
- [ ] Runtime identity cannot create/drop production schemas.
- [ ] Backup/restore test passes and recovery objectives are approved.
- [ ] Transactional outbox leasing, idempotency, poison-message handling, and restart recovery are tested.
- [ ] Health/readiness, metrics, alerts, capacity, load, and concurrency requirements are approved.
- [ ] PHI-safe logging and audit review process are validated.
- [ ] Support/on-call ownership, provider outage runbook, directory failure runbook, and database restore runbook are trained.
- [ ] Accessibility review is complete.

## Go/no-go authorization

- [ ] Scope is limited to the approved department/workflow/pilot group.
- [ ] Written hospital authorization exists.
- [ ] Clinical safety, privacy, security, legal, IT, operational, and product owners sign off.
- [ ] The pilot has success metrics, training, support contacts, incident response, and a rollback/downtime plan.
