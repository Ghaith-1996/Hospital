# Phase 9 implementation and verification ledger

Plan: `plans/2026-09-22-phase-9-escalation.md`. Baseline `9e312b2`; branch `feature/alerts-and-escalations`. Started 2026-09-22. Phase 9 is in progress, not complete.

## Authority and decisions

- User explicitly authorized the supplied Phase 9 specification and sequential implementation. Earlier Phase 8.5 scope statements are historical; no hospital policy or retrospective acceptance is inferred.
- Reuse existing policies, directory revision calculation, alert mutation lock, transactional outbox, channels and connected UI. Ponytail's reuse/minimal-change guidance is subordinate to all safety and test requirements.
- Task 2 includes its additive snapshot storage prerequisite; exhaustive run persistence remains task 4. Cost: two additive migrations instead of one; approval is testable before enabling automation.
- The external master build plan was not found in the repository or available attachment/document search. Existing translated Phase 0 architecture/ADRs and the complete supplied Phase 9 plan are the available specification; no missing production decisions are inferred.

## Environment and commands

- .NET SDK `C:\Users\ghait\.dotnet\dotnet.exe`: 10.0.100; Node 24.16.0.
- Started existing Docker Desktop; engine 29.5.2 responds.
- `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo`: pass, all 11 projects. Log: temporary `hospital-phase9-restore.log`.

## Task status

1. Complete in `df6c960`: specification/safety boundary. Documentation-only; `git diff --check` passed.
2. Exact review/confirmation slice in verification. Added optional wire fields that are mandatory for new successful confirmations, safe conflicts, database-clock review, locked evidence, immutable exact-version snapshot storage, additive migration `20260922144312_Phase9ConfirmedEscalationPlan`, and connected review display/request binding. No worker automation yet.
   - RED: two PostgreSQL tests failed because missing approval was accepted (200 rather than 409) and review lacked `escalationPlan`.
   - RED: snapshot SQL mutation succeeded before the immutable-history trigger; failed as expected once Docker recovered. One preceding test run was an environment failure (Docker unavailable), not a behavioral RED.
   - RED: frontend future-backup visibility test failed; four retained tests passed. Initial sandbox startup failure was rerun with authorized filesystem access.
   - GREEN: 128 API integration tests, 67 infrastructure tests, 60 domain tests, 29 frontend tests (8 files); no failures/skips in these suites. Typecheck and lint passed. Infrastructure regression took 17m17s including container/database work.
   - `dotnet format ... --no-restore`, OpenAPI regeneration, sensitive-data scan, active storage-safety scan and `git diff --check` passed. System replay scenario now sends the exact reviewed plan; system execution remains a later gate.
3. Domain run behavior: 10 new executable tests first failed against unimplemented methods, then the complete domain project passed 70/70. Exact alert version, precedence, UTC, pause/resume remaining delay, negative-response consumption, exhaustion and lease recovery are covered. Persistence mapping follows in task 4; this slice alone does not enable a worker.
4–12. Pending.

## Final gate

Not yet run. No completion, migration, test-count, security or system-restart claim is made until recorded with actual evidence. Production decisions remain `REQUIRES_HOSPITAL_DECISION` as listed in the spec.

### Task 4 — durable persistence
Added nullable exact-version binding (legacy rows remain disabled), durable leases/pause delay, scoped uniqueness and append-only safe events. PostgreSQL tests first failed because policy/event mutation was allowed, then passed with database triggers. Full infrastructure suite: 70 passed, no skips. No historical migration changed. New migration is additive; policy activation status alone remains editable.

