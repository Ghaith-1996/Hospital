# Agent Operating Rules

This file governs all future work in this repository. Read it and the relevant `docs/` files before changing anything.

## Current phase

Phase 9 implementation and local technical verification are complete as of 2026-09-14. The [verification and human review package](docs/superpowers/phase9-verification.md) records the delivered scope, 491 backend tests, 38 web tests, 12 connected scenarios, standalone browser and container checks. Final project-owner acceptance remains pending. Do not start Phase 10 without explicit authorization.

The active phase is Phase 9 escalation, explicitly authorized by the project owner's 2026-09-07 request and specified in `docs/superpowers/specs/2026-09-07-phase-9-escalation-design.md`. This supersedes the Phase 8.5 prohibition on starting Phase 9; historical approval/verification evidence remains unchanged and no missing acceptance is inferred. Preserve the connected Phase 8.5 frontend: PostgreSQL owns saved workflow state, backend development authentication owns identity/organization, and the worker performs simulation dispatch. Browser memory holds only unsaved edits and transient presentation; no persistent browser workflow storage or parallel frontend state machine.

Phase 9 is Development/Test-only, exact-human-confirmed DEMO escalation. Review must show immutable policy ID/version, steps and every future backup/channel; confirmation must verify the plan revision and reject changed policy/directory/on-call evidence. Legacy alerts without exact snapshots are ineligible. Workers activate only preconfirmed backups, use PostgreSQL UTC time, reuse identifier-only outbox dispatch, and never choose replacements. Acceptance/lifecycle/override/escalation transactions share alert-row-before-run lock order; decline/unavailable signals are consumed once durably. An inactive preconfirmed backup produces visible failure/manual fallback, never replacement. Preserve API, migration, identity, data-protection, lifecycle and concurrency safeguards and all prior safety tests. Use additive migrations only.

The repository must remain public; do not change visibility/settings. No real providers, external callbacks, production identity, hospital integration, AI, real data, production escalation period or Phase 10. Production lifecycle, responsibility, escalation timing/authority and fallback remain `REQUIRES_HOSPITAL_DECISION`. Stop after Phase 9 for human review.

The approved phase is the only phase in scope. At the end of every phase, report files changed, decisions made, commands run, test results, limitations, human actions, and a proposed commit message, then stop for review.

## Instruction precedence

1. System and developer instructions.
2. The user's mandatory project rules.
3. This file and the approved repository documentation.
4. The attached master build plan as a baseline specification and work plan.

The master plan does not create hospital policy. A recommendation from that document is a simulation proposal unless an authorized human approves it. Use `REQUIRES_HOSPITAL_DECISION` for any missing real workflow, escalation, privacy, security, identity, directory, communications, retention, hosting, or integration decision.

## Safety invariants

- Use fictional hospital, employee, practitioner, patient, phone, and clinical data only.
- Never add real PHI, employee data, contact data, credentials, tokens, or sensitive screenshots to code, tests, fixtures, logs, analytics, issue trackers, or documentation.
- AI may transcribe or format source content and identify uncertainty or missing fields. AI must not diagnose, assign urgency, select final recipients, change critical values, stop escalation, or dispatch autonomously.
- Dispatch requires an authenticated authorized human confirmation of the exact alert version, approved message, critical values and units, recipients, channels, exact policy ID/version, and future escalation plan/revision shown for confirmation.
- Any content or recipient edit invalidates the previous approval and requires reconfirmation.
- Preserve original typed/transcribed source, structured suggestions, and operator-approved content as separate records.
- Every critical number and unit remains unresolved until explicitly confirmed by a human.
- SMS and voicemail contain generic wake-up wording only by default; full case details remain in the authenticated interface.
- Keep delivered, opened, acknowledged, and responsibility accepted as separate states.
- When a channel cannot produce a state, record `NotApplicable`; do not confuse it with pending, failed, delivered, opened, acknowledged, or responsibility accepted.
- External callbacks are untrusted, authenticated, validated, replay-resistant, idempotent input.
- Failed deliveries and provider outages remain visible to the operator.
- Never commit secrets. Development secrets belong only in ignored local configuration or an approved secret store.

## Product and architecture constraints

- Build a modular monolith with explicit module boundaries.
- Use C# 14, ASP.NET Core/.NET 10 LTS, EF Core 10, Npgsql 10, PostgreSQL 18, TypeScript, React, Next.js App Router, and Node.js 24 LTS.
- Use a transactional outbox for asynchronous dispatch.
- Use TDD and real PostgreSQL integration tests with Testcontainers; an in-memory database is not a substitute for relational behavior.
- Use UTC, optimistic concurrency for editable drafts, organization scoping, RFC 7807 problem details, correlation IDs, and idempotency keys for side-effecting operations.
- Keep provider interfaces separate from simulated and approved real implementations.
- Keep the product usable with AI, speech, SMS, voice, hospital identity, and hospital directory providers disabled.

## Documentation rules

- Read the relevant product, architecture, ADR, and security documents before implementation.
- Update documentation and tests in the same phase as behavior changes.
- Explain whether each new behavior is a simulation-only assumption or an approved human decision.
- Do not write a production escalation period, clinical policy, privacy conclusion, or integration mapping without hospital approval.
- Use the exact marker `REQUIRES_HOSPITAL_DECISION` for missing hospital decisions.
- Keep examples fictional and visibly synthetic.

## Review and verification

Before claiming a phase complete:

- Check that no out-of-scope phase work was added.
- Check for secrets and sensitive data.
- Check that all safety invariants have a documented test or explicit Phase 0 design control.
- Run the relevant format, build, test, typecheck, lint, integration, and security checks for the phase.
- State anything not run and why.
