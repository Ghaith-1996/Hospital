# Agent Operating Rules

This file governs all future work in this repository. Read it and the relevant `docs/` files before changing anything.

## Current phase — Phase 10 authorized

The owner explicitly authorized Phase 10 on September 19, 2026 from completed Phase 9 commit `44f18535ca202333ea526046f50531331cc3d721`. This supersedes historical Phase 9 stop instructions below for Phase 10 only, without inferring earlier final acceptance. Follow the [Phase 10 design](docs/superpowers/specs/2026-09-19-phase-10-audit-observability-design.md) and [plan](docs/superpowers/plans/2026-09-19-phase-10-audit-observability.md): scoped audit, append-only storage, local structured logs/metrics, truthful health, safe warnings, runbooks and real fictional PostgreSQL restore verification. Use TDD and focused commits. No external exporter, real provider, hospital integration, invented production decisions or Phase 11. Stop for owner review.

## Historical Phase 9 baseline

The active phase is Phase 9 simulation-only escalation, authorized by the project owner's supplied Phase 9 plan and instruction to implement it on 2026-09-22. See `docs/superpowers/specs/2026-09-22-phase-9-escalation-design.md`. Start from the Phase 8.5 reintegration at `9e312b2`. PostgreSQL owns saved workflow state and UTC scheduling; the existing worker activates only the exact future DEMO recipients reviewed and confirmed by the operator, through the transactional outbox. Backend development authentication owns identity and organization. Browser memory holds only unsaved edits and transient presentation state; do not restore persistent browser workflow storage or a parallel frontend state machine. Preserve the API, historical migrations, identity, data-protection, lifecycle, and concurrency safeguards. The repository must remain public; do not change repository visibility/settings. Do not add real providers, external callbacks, production identity, hospital integration, AI, real data, or Phase 10 behavior. Production timing, authority, lifecycle, responsibility, escalation, and fallback policy remain `REQUIRES_HOSPITAL_DECISION`.

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
- Dispatch requires an authenticated authorized human confirmation of the exact alert version, approved message, critical values and units, recipients, channels, and policy version shown for confirmation.
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
