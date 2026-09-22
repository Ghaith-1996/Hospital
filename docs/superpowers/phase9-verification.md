# Phase 9 implementation and verification ledger

Plan: `plans/2026-09-22-phase-9-escalation.md`. Baseline `9e312b2`; branch `feature/alerts-and-escalations`. Started 2026-09-22. Phase 9 is in progress, not complete.

## Authority and decisions

- User explicitly authorized the supplied Phase 9 specification and sequential implementation. Earlier Phase 8.5 scope statements are historical; no hospital policy or retrospective acceptance is inferred.
- Reuse existing policies, directory revision calculation, alert mutation lock, transactional outbox, channels and connected UI. Ponytail's reuse/minimal-change guidance is subordinate to all safety and test requirements.
- Task 2 includes its additive snapshot storage prerequisite; task 4 adds runs/events. A third additive migration closes published-policy step insertion. Approval is testable before enabling automation; historical migrations are unchanged.
- The external master build plan was not found in the repository or available attachment/document search. Existing translated Phase 0 architecture/ADRs and the complete supplied Phase 9 plan are the available specification; no missing production decisions are inferred.

## Environment and commands

- .NET SDK `C:\Users\ghait\.dotnet\dotnet.exe`: 10.0.100; Node 24.16.0.
- Started existing Docker Desktop; engine 29.5.2 responds.
- `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo`: pass, all 11 projects. Log: temporary `hospital-phase9-restore.log`.

## Task status

1. Complete in `df6c960`: specification/safety boundary. Documentation-only; `git diff --check` passed.
2. Complete in `5468d16`. Added optional wire fields that are mandatory for new successful confirmations, safe conflicts, database-clock review, locked evidence, immutable exact-version snapshot storage, additive migration `20260922144312_Phase9ConfirmedEscalationPlan`, and connected review display/request binding. This slice alone did not enable worker automation.
   - RED: two PostgreSQL tests failed because missing approval was accepted (200 rather than 409) and review lacked `escalationPlan`.
   - RED: snapshot SQL mutation succeeded before the immutable-history trigger; failed as expected once Docker recovered. One preceding test run was an environment failure (Docker unavailable), not a behavioral RED.
   - RED: frontend future-backup visibility test failed; four retained tests passed. Initial sandbox startup failure was rerun with authorized filesystem access.
   - GREEN: 128 API integration tests, 67 infrastructure tests, 60 domain tests, 29 frontend tests (8 files); no failures/skips in these suites. Typecheck and lint passed. Infrastructure regression took 17m17s including container/database work.
   - `dotnet format ... --no-restore`, OpenAPI regeneration, sensitive-data scan, active storage-safety scan and `git diff --check` passed. System replay scenario now sends the exact reviewed plan; system execution remains a later gate.
3. Domain run behavior: 10 new executable tests first failed against unimplemented methods, then the complete domain project passed 70/70. Exact alert version, precedence, UTC, pause/resume remaining delay, negative-response consumption, exhaustion and lease recovery are covered. Persistence mapping follows in task 4; this slice alone does not enable a worker.
4. Complete in `47cd83a`: durable run/event persistence.
5–7. Complete in `9ff96f6`: database scheduling, activation and outbox.
8–10. Complete in `b0aa4f0`: overrides, projection and UI; reviewed recovery fixes recorded below.
11–12. System scenarios and final verification in progress.

## Final gate

Running on the final reviewed code. No phase completion or system-restart claim is made until recorded with actual evidence. Production decisions remain `REQUIRES_HOSPITAL_DECISION` as listed in the spec.

### Task 4 — durable persistence
Added nullable exact-version binding (legacy rows remain disabled), durable leases/pause delay, scoped uniqueness and append-only safe events. PostgreSQL tests first failed because policy/event mutation was allowed, then passed with database triggers. Full infrastructure suite: 70 passed, no skips. No historical migration changed. New migration is additive; policy activation status alone remains editable.


### Tasks 5–7 — scheduler, activation and outbox
Implemented PostgreSQL clock scheduling and recoverable claims, alert-before-run locking, exact approved backup activation and atomic identifier-only outbox. Activation and scheduling were tested together because they share the durable transaction. Three scheduler tests: two behavioral RED then three GREEN. Eight scheduler/dispatch tests exercise concurrency, restart leases, legacy exclusion, accepted responsibility, acknowledgement and negative response behavior. A dispatch test exposed original-recipient leakage into escalation; original and escalation jobs now select disjoint approved selections. Full infrastructure regression: 78 passed, no skips. Worker escalation is explicitly disabled by default and fails closed outside Development/Test or without simulated dispatch. No new provider or dependency.


### Tasks 8–10 — controls, safe live projection and connected UI
Pause/Resume reuse lifecycle authorization, locking, idempotency and compact result references. Five PostgreSQL tests verify validation, replay, persisted delay, restart, read-only projection and isolation. API test verifies permitted/rejected actions, version conflicts and replay. Frontend test failed for missing pause control, then passed with same-key uncertain retry/double-submit protection. Full backend regression: Domain 70, Application 39, Architecture 9, API 129, Infrastructure 83 (330 total, no skips). Web: 30 passed; typecheck/lint passed. OpenAPI regenerated. Follow-up dispatch lock tightening will be covered with restart/concurrency verification.

### Follow-up policy, concurrency and review evidence

- A distinct immutable `DEMO-9` policy replaces the seed's legacy `DEMO-1` semantics for new reviews; no historical version is rewritten. Expected version test failed before the new seed. PostgreSQL initially allowed inserting a step after policy approval; the new focused test failed, then passed with `20260922165412_Phase9PublishedPolicySteps`. All 16 escalation integration tests passed at that checkpoint.
- Four additional PostgreSQL tests cover acceptance holding the alert lock while a due worker waits, plus suppression of already queued backup delivery after acceptance, resolution and cancellation. All 12 processor tests passed at that checkpoint. API checks additionally cover anonymous rejection, cross-organization 404, RFC7807 content type and protected-value non-disclosure; four focused API/review tests passed.
- Fresh read-only review through `superpowers:requesting-code-review` identified three Important findings. Each was reproduced: disappeared approved role caused an FK failure with no durable failure state; backup outage failed the whole previously delivered alert; changed live controls hid retry after a committed-but-lost Pause response.
- Fixes validate exact stored references under database locks and durably fail without substitution; retain the Active original recipient response path when backup delivery fails; and expose retained-command retry independently of current permissions. Review recheck found all three resolved and no remaining actionable issue within that recheck. Final infrastructure regression: 89 passed, no skips. Web regression: 31 passed in eight files.
- First system attempt could not launch the mismatched installed Playwright 1.55.1. Root `npm ci` restored pinned 1.60.0 and its Chromium was installed. A subsequent attempt incorrectly reused a production build containing the previous random API port; that run was discarded and its isolated services were verified removed. Final system verification always rebuilds for its own API address.
- Required dependency audit found the pre-existing Next.js 16.3.1 and transitive sharp vulnerabilities. Narrow lockfile update pins Next.js/eslint-config-next 16.3.6 and resolves sharp 0.35.4. References: [Next.js Windows advisory](https://github.com/vercel/next.js/security/advisories/GHSA-p293-qw3h-jr36), [Next.js AVIF advisory](https://github.com/vercel/next.js/security/advisories/GHSA-2xp9-vwfh-vxw4), [sharp advisory](https://github.com/lovell/sharp/security/advisories/GHSA-rgj7-g3m4-5g8c). Production npm audit now reports zero vulnerabilities; NuGet audit reports none across all 11 projects. No new direct package was introduced.

