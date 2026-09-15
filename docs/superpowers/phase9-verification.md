# Phase 9 verification and human review package

Date: 2026-09-14 America/Toronto (final runs extend into September 15 UTC). Status: implementation and local technical verification complete; ready for project-owner review. Technical verification does not establish human acceptance or authorize Phase 10.

## Authority and source

The owner supplied the Phase 9 plan on September 7, authorized implementation, and requested continuation on September 14. The later instruction to work without subagents was followed: remaining implementation, verification and final review were performed by the primary agent. Earlier slice reviews remain historical evidence. No final human acceptance, publishing, push, merge or tag authorization is inferred.

The approved [design](specs/2026-09-07-phase-9-escalation-design.md) and [plan](plans/2026-09-07-phase-9-escalation.md) govern the change. The separate original master build plan was unavailable in the attachment/tracked sources; it was not independently read. Baseline is `9e312b2`; work is isolated on `feat/phase-9-escalation` in `D:/hospital/phase9-escalation`. Original checkout remains separate. Repository visibility/settings were unchanged.

## Delivered behavior and decisions

- Exact review includes immutable policy ID/version, plan revision, trigger/stop semantics, ordered steps, delay/attempt caps and every future backup practitioner/role/channel with directory/on-call evidence. Confirmation requires the exact version, revision and idempotency key. Changed evidence rejects the entire confirmation. Later mutable policy edits cannot alter a confirmed plan.
- PostgreSQL UTC drives scheduling, claims, execution and remaining pause delay. One exact-version run and alert-before-run locks serialize worker, acceptance, lifecycle and override operations. Each acquisition has its own lease token. Activation, signal consumption, timeline, audit, step advance and outbox commit atomically.
- Only preconfirmed recipients activate. Inactive/invalid members fail the whole step visibly, with no replacement. Future snapshots grant no inbox access before activation. Legacy missing bindings remain ineligible and are never backfilled.
- Delivered/opened/acknowledged/call-unit remain distinct from responsibility. Active exact-version responsibility, Resolve and Cancel stop escalation; declined/unavailable responses expedite one step per durable signal. Pause preserves remaining delay and pending signals. Resume restores the interval. Exhaustion means automatic steps have been queued; delivery and responsibility remain separate.
- Pause/Resume reuse the existing lifecycle-operator policy (Operator, Administrator, ClinicalSupervisor and SystemAdministrator), scoped version checks, bounded keys and allowlisted reasons. Auditor may read live status but cannot execute lifecycle or escalation controls. Replay returns the original immutable result. Live uses a consistent read and server-authorized action flags. Browser polling is read-only; uncertain retries retain the original key/body and block new commands.
- `EscalationDispatchRequested` contains exactly `alertId`, `alertVersion`, `escalationRunId`, `stepSequence`, `recipientSelectionIds`. Existing simulated dispatch handles only the newly activated selections, respects the captured attempt cap and checks responsibility/lifecycle before pending delivery. Initial dispatch excludes policy-added selections.
- The seeded Jules Martin backup, SecureMessage, 60-second delay and one attempt are explicitly fictional DEMO choices. No production interval, hierarchy or clinical policy is established.

## Files and migrations

| Area | Main changes |
|---|---|
| Domain | `Domain/Escalation/*`, `Domain/Delivery/EscalationRun.cs`, exact bindings on `Domain/Alerts/Alert.cs`, identifiers/enums/policy semantics |
| Application/API | Review and live contracts, escalation options/guard/control contracts, `Http/EscalationEndpoints.cs`, authorization-masked live endpoint, generated `docs/api/openapi.json` |
| Infrastructure | `Infrastructure/Escalation/*`, review confirmation, outbox dispatcher, shared mutation clock/locks, response/lifecycle services, consistent live projection, EF mappings/immutability enforcement |
| Worker | Guarded `SimulationEscalation` configuration, scoped scheduler/processor hosted service alongside simulated dispatch |
| Frontend | Typed review/control/live client, exact review, durable escalation status/provenance/timeline, retained uncertain command retry and alert-identity reset |
| Tests/harness | Domain/API/PostgreSQL escalation suites, existing safety regression extensions, 9 frontend cases, 9 connected cases, shared E2E helpers and owned worker supervisor/control files |
| Verification/docs | TRX output in `scripts/test-all.ps1`, plan/checklists, workflow, data/state/review/dispatch architecture, threat/logging boundaries and this report |

Backend paths above are under `src/backend/CriticalAlerts.*`; all changed paths can be reproduced with `git diff --name-status 9e312b2 HEAD`. No historical migration was edited. Added migrations (each with its generated Designer and updated model snapshot):

1. `20260907215641_Phase9EscalationSnapshots`
2. `20260907224148_Phase9Escalation`
3. `20260908002524_Phase9EscalationOverrideReason`

Fresh PostgreSQL 18 databases were migrated and explicitly reset using the harness's `database migrate` and `database reset-demo --confirm-demo-reset` commands. This is the approved equivalent of the database scripts against isolated loopback simulation databases with per-run credentials. The legacy migration test starts at the pre-Phase-9 schema, upgrades an existing confirmed alert, preserves its protected value and proves no fabricated binding/eligibility.

## Verification commands and results

Pinned tools: .NET SDK 10.0.100, Node 24.16.0/npm 11.13.0, PostgreSQL 18.4. Local PATH uses `D:/hospital/Hospital/.dotnet10` and `D:/hospital/.node-v24.16.0`; Playwright uses the installed Chromium headless shell revision 1223 at `D:/hospital/Hospital/.playwright-browsers`. Command-scoped Git safe-directory configuration accommodates the sandbox/worktree owner; no global Git setting was changed.

Final gate: `./scripts/test-all.ps1`. Its final backend stages passed; the gate then stopped at a newly reported web dependency audit failure. After the targeted package fix, the unchanged remaining stages were resumed from `npm.cmd ci --no-audit --no-fund` through container/proxy verification. This is a staged completion, not a claim that the interrupted invocation exited successfully. No unavailable check is represented as passing.

| Check | Result |
|---|---|
| `dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo` | PASS |
| `dotnet format src/backend/CriticalAlerts.sln --verify-no-changes --no-restore --verbosity minimal` | PASS |
| `dotnet build src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo` | PASS, zero warnings/errors |
| `dotnet list src/backend/CriticalAlerts.sln package --vulnerable --include-transitive --no-restore` | PASS, no vulnerable packages reported by configured sources |
| `./scripts/verify-openapi.ps1` | PASS, complete OpenAPI 3.1 runtime contract matches |
| Sensitive-data and active web storage scripts | PASS, including final documentation rescan |
| Full backend `dotnet test ... --configuration Release --no-build --nologo --logger trx` | PASS, 491/491; zero failed/skipped |
| Root/web locked `npm ci`, web production dependency audit | PASS, zero reported vulnerabilities |
| Web unit tests, typecheck, lint, production build | PASS, 38/38 tests across nine files; Next.js 16.3.5 |
| Standalone Playwright | PASS, 1/1 |
| Connected system Playwright | PASS, 12/12 together in 2.3 minutes; no retries |
| API/worker/web container builds and web container API proxy | PASS; built web image forwards path/query to a separate synthetic API container; resumed gate exited 0 |
| Final branch diff, UTF-8, local documentation links and safety scans | PASS; strict UTF-8 on 372 source/document files and 55 local Markdown links |

Focused evidence already passed: 38 web tests across nine files; seven PostgreSQL lock/rollback/concurrency cases; all twelve connected cases across targeted development runs; standalone browser 1/1. A new deterministic final-clock rollback regression plus the three affected infrastructure cases passed 4/4, and the affected API provenance case passed 1/1. These focused results do not replace the final full-gate rows above.

Final backend TRX counts: Domain 92, Application 46, Architecture 9, Infrastructure 175, API Integration 169. Reports are under each project's `TestResults/ghait_GHAITH_2026-09-14_21_17_52.trx` or `...21_17_53.trx`; counters were parsed and verified. The local command transcripts are in `D:/hospital/phase9-verification/`. This committed report contains the relevant results so review does not depend on ignored local handoff files.

## Connected proof

The system harness owns a fresh PostgreSQL container, Test API, production Next server and Node worker supervisor. Each worker enables both dispatch and escalation. Tests communicate through local control files, never an application control endpoint. Every worker PID is recorded, shutdown is requested, owned-process cleanup is a fallback, and container/process/port teardown is checked even on failure. A web proxy preflight catches builds pointing to the wrong API. Worker control file rename retries are limited to transient Windows sharing errors.

The final combined run passed all twelve cases. The two-worker proof recorded PIDs `32340` and `25084`, both handlers enabled, and cardinality one. Across the run, 33 owned worker starts exercised restart boundaries. Teardown reported `container_remaining=0 live_owned_processes=0 live_worker_processes=0 ports_closed=1`. Local logs are under `C:/Users/ghait/AppData/Local/Temp/critical-alerts-system-4bbece1d65244d6e81d317dfce5d21dc`.

| Scenario | Durable assertion |
|---|---|
| A/B/C retained | Browser draft/review/dispatch/response/resolution; stale edit rejection; concurrent same-key exact confirmation creates one initial logical delivery set |
| D | Exact browser approval of Jules and plan, database deadline, one backup selection/outbox/delivery and visible provenance/timeline |
| E | Acknowledgement creates no responsibility and does not prevent backup delivery |
| F | Acceptance before due survives worker restart and stops the run with zero backup effects |
| G (both types) | Declined and Unavailable each activate before a deliberately future deadline; one consumed signal remains one after restart/polls |
| H | Positive delay preserved across pause/restart/past original deadline; no activation while paused; resumed due equals event time plus saved interval, then one delivery |
| I | Two distinct live worker processes across the same due interval; one activation, step outbox and logical attempt, with other timeline event types retained |
| Inactive backup | Visible ProcessingFailed/manual fallback, zero replacement/partial activation |
| Changed review/legacy | Changed evidence returns 409 with zero confirmation/outbox; historical shape remains ineligible without a run |

Test-only due-time changes are scoped to their fictional run while workers are stopped. Immutable confirmed snapshots are not edited. The separate PostgreSQL tests cover multiple-step signal order, both lock-winning orders, saved-effect rollback, lease expiry/stale ownership, organization boundaries and inactive/missing/foreign/channel-ineligible membership.

## Failures investigated and resolved

- The September 14 production dependency audit found vulnerable Next.js 16.3.1 and sharp. The maintained fixes were verified against the [Next Windows advisory](https://github.com/vercel/next.js/security/advisories/GHSA-p293-qw3h-jr36), [Next image-optimization advisory](https://github.com/vercel/next.js/security/advisories/GHSA-2xp9-vwfh-vxw4) and [sharp advisory](https://github.com/lovell/sharp/security/advisories/GHSA-rgj7-g3m4-5g8c). Next and its ESLint configuration are now pinned to 16.3.5; the lock resolves sharp 0.35.4 through Next's patched optional dependency range. React and unrelated direct pins remain unchanged. The immediate production audit reports zero vulnerabilities; all affected web/container stages passed as recorded above. No broad `audit fix --force` was used.

- The frontend production build exposed five isolated non-UTF-8 separator bytes in the prior exact-review slice. Correct UTF-8 restored identical visible text; the build and 38 web tests passed afterward.
- In-app Browser startup failed before execution with `failed to write kernel assets: The system cannot find the path specified. (os error 3)`. The already-authorized Playwright fallback verified rendered desktop/mobile controls, exact request bodies, zero console errors and zero horizontal overflow; screenshots were visually inspected. This was synthetic API UI QA, separate from real connected proof.
- Initial connected setup reused a build targeting the old API port. The known-invalid run was stopped and cleaned up; subsequent runs rebuilt against their own API, and the harness now probes that proxy. A new SQL assertion was corrected to join the actual `recipient_selection_id`. Windows control-file rename contention received bounded atomic retries.
- The existing deterministic simulated provider can persist a delivered timestamp one second ahead. Positive control fixtures now wait for PostgreSQL time to reach that evidence. This preserves the runtime clock guard.
- Earlier PostgreSQL tests measured actual backward wall-clock readings around 70–76ms; their host/VM cause remains unestablished. The first final backend gate had four related failures: stale time at lease release, resolution immediately after acceptance, and two recovery fixtures assuming every immediate claim/poll executes. The final repository change rolls back saved event/outbox/run effects, clears tracking and returns a deferral if the final database time predates the staged run update. It retains the original durable acquisition for valid retry. A deterministic regression first reproduced the exception, then proved zero committed effects and safe retry. Only affected positive fixtures allow bounded no-work or exact clock-conflict retries; keys, payloads, lock proof, negative guards and cardinality assertions are preserved. No timestamp is clamped or fabricated.

## Review, limitations and human gate

The primary reviewed confirmation binding/revalidation, immutable storage and scoped keys, alert-before-run lock order, stop precedence, exact signal consumption, acquisition fencing/rollback, outbox membership/caps/suppression, safe live projection and actor flags, browser state/retry behavior, migration history and harness cleanup. Earlier tasks 1–9 also have historical slice review records. No further agents were used after the owner's instruction.

No unresolved actionable finding remained in the final self-review. Final audit fixes are isolated in `33d3900` (database-clock rollback) and `5913982` (Next.js dependency patch). The dependency lock update changes Next/sharp platform packages and a compatible transitive fastq patch; unrelated direct dependencies are unchanged. The tested backend source is unchanged after its 491-test pass, and the patched web dependencies were verified through unit, browser and Linux-container stages.

Protected-value tests exercise live/API errors, logs, audit, idempotency storage and strict outbox/event allowlists. No real provider, callback, hospital connector, production identity, AI, clinical selection, real data or Phase 10 was introduced. Completed exhaustion never claims clinical resolution. General production resilience, throughput, external-provider delivery guarantees and time synchronization are not established by the fictional simulation.

Known bounded tradeoffs: SHARE evidence table locks favor DEMO correctness over directory-write throughput; practitioner roles have no separate active/validity interval, so exact membership plus active practitioner/usable endpoint is checked. PostgreSQL JSONB normalizes duplicate properties before persisted parsing, while the typed producer emits only canonical fields. A clock deferral may wait for durable lease recovery in the running worker. No Linux/remote GitHub CI run was performed in this task; local Windows/PostgreSQL and rebuilt Linux container checks are reported separately.

All production trigger/urgency rules, delays/retries, backup hierarchy, on-call/directory freshness and role validity, override authority, acceptance/transfer/lifecycle semantics, fallback routing, permitted data, privacy/retention/deletion, audit ownership, identity/security, hosting/residency, provider contracts and hospital integrations remain `REQUIRES_HOSPITAL_DECISION`.

Proposed closure commit: `docs: complete phase 9 verification package`. Proposed tag for separate owner decision: `phase-9`; no tag created. Human action: review and accept the Phase 9 simulation package and explicitly authorize any next phase. No push, merge, deployment or Phase 10 is included.
