# Phase 12 verification: slice 1, ACS SMS

Status: slice 1 is implemented and locally verified with fake transports and test tokens. The project owner's review is pending. No live Azure request was made. Voice is not started.

## Baseline and scope

- Branch: `feature/phase-12-real-communication-adapter`, from `main` at `9209b41`. It was later merged with `main` at `32641a3` (cleanup PRs #10 and #11) and `c7c5395` (CI PRs #12 and #13).
- Phase 11 was merged through PR #8 before this branch. Post-merge `main` was checked: backend 393/393 and web 38/38 passed, and typecheck was clean.
- Design and failure modes F1–F43: [the slice 1 design](specs/2026-09-29-phase-12-acs-sms-adapter-design.md).
- Architecture: [real communication adapters](../architecture/real-communication-adapters.md).

## PR #9 review verification (2026-10-07)

The three requested review findings were reproduced against `ef24f1d` with real PostgreSQL E2E tests before implementation:

- a subscription-writer JWT with the webhook role reached the endpoint instead of receiving `403`;
- one worker fault after successful delivery-report polls permanently failed the outbox;
- a 121-second delay between the pass-start clock read and the first send failed a never-sent attempt as uncertain.

The fixes published in `73864c1` require the configured Event Grid sender, count worker failures separately from lease claims, and read the per-recipient first-send clock after the claim and alert lock. This follow-up adds coverage for two consecutive worker faults still exhausting the bounded budget, a writer forging a report with the actual message ID and tag, conflicting sender claims in both directions, and missing or invalid sender configuration. An all-zero sender GUID also refuses startup. The parsed sender GUID is retained in canonical form, so surrounding configuration whitespace cannot pass startup and then reject a legitimate token; this compatibility regression also failed before its fix.

| Check | Result |
| --- | --- |
| Locked restore | Passed |
| Full backend suite, final tree | 480/480 passed: Domain 40, Application 55, Architecture 6, API 226, Infrastructure 153; none skipped |
| ACS E2E suite within the API run | 28 passed, producing 24 evidence scenarios |
| Release build | Passed, 0 warnings / 0 errors |
| `dotnet format --verify-no-changes --no-restore` | Passed |
| `verify-no-sensitive-data.ps1` | Passed |
| `verify-observability-safety.ps1` | Passed: 51 API and 10 infrastructure checks |
| `verify-openapi.ps1` | Complete runtime contract matches |
| `dotnet list package --vulnerable --include-transitive --no-restore` | No vulnerable packages reported |

Repeat with the pinned .NET 10.0.100 SDK and a running local Docker engine:

```powershell
dotnet restore src/backend/CriticalAlerts.sln --locked-mode --nologo
dotnet test src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo
dotnet build src/backend/CriticalAlerts.sln --configuration Release --no-restore --nologo
dotnet format src/backend/CriticalAlerts.sln --verify-no-changes --no-restore
pwsh -NoProfile -File scripts/verify-no-sensitive-data.ps1
pwsh -NoProfile -File scripts/verify-observability-safety.ps1
pwsh -NoProfile -File scripts/verify-openapi.ps1
dotnet list src/backend/CriticalAlerts.sln package --vulnerable --include-transitive --no-restore
```

Evidence remains in `TestResults/phase12/acs-sms-e2e-evidence.json`: writer tokens receive `403` without changing attempt, inbox, event or audit rows; the configured v2 sender can deliver that same report; report polls permit a later valid report after one worker fault, while two actual faults remain bounded. The send-delay case uses the refreshed first-send time. No live Azure service was called. Browser, web, restore and container-image checks were not rerun for this backend review follow-up; the whole Phase 12 gate and live staging remain separate.

Earlier runs had intermittent failures in `ObservabilityWorkflowTests.RealWorkflowLogsMetricsProblemsHealthAndAuditExcludeProtectedSentinels` and `EscalationProcessorTests.DueWorkerWaitsForConcurrentAcceptanceAndThenStopsWithoutActivation`. A diagnostic captured `no-work` in the observability test, before its sentinel-throwing adapter ran: confirmation timestamps the outbox with PostgreSQL time, while the test immediately polls with the host clock. That test now waits at most one second, only while the outcome is `no-work`; all other unexpected outcomes still fail immediately. The escalation race now schedules its due run with PostgreSQL time, matching the processor's claim clock, so host/DB skew cannot skip the concurrent-acceptance scenario. Both focused tests and the subsequent full 480-test suite passed. These changes correct test scheduling assumptions without changing production workflows or weakening the failure and no-activation assertions.

## Earlier comprehensive-audit checks (2026-10-06, F37–F43)

| Check | Result |
| --- | --- |
| `dotnet format --verify-no-changes` | Passed |
| Release build | Passed, 0 warnings / 0 errors |
| Domain / Application / Architecture tests | 40 / 55 / 6 passed |
| API integration tests, including 21 Phase 12 E2E tests (21 evidence scenarios) and the OpenAPI contract comparison | 219 passed |
| Infrastructure tests, including 61 ACS contract tests | 153 passed (this run included the usually flaky escalation tests) |
| Web unit tests / typecheck / lint | 111 passed in 10 files (73 in the new audit vocabulary contract) / clean / clean |
| Audit-supplied auth harness (run in a scratch copy) | Before the fix (`ef24f1d`): subscription-writer v1 `appid`, v2 `azp` and missing-sender tokens with the shared role were authorized. After: those three are refused, the configured Event Grid sender (v1 and v2) is authorized, and the missing-role control is refused |
| `verify-no-sensitive-data.ps1`, `verify-observability-safety.ps1` | Passed (2026-09-29) |
| Web storage-safety scan | Passed; its patterns were run manually because the script requires PowerShell 7, which is not installed here |
| `verify-openapi.ps1` | Not run (needs PowerShell 7). The runtime comparison in `OpenApiContractTests` passed, and the webhook is excluded from the client contract. |

**Escalation flakes.** Intermittent failures occur in `EscalationProcessorTests`: `DueWorkerWaitsForConcurrentAcceptanceAndThenStopsWithoutActivation` and `QueuedBackupDispatchRechecksDurableStopConditions`. They predate these changes:

- they fail on untouched `main`;
- they failed in 2 of 4 isolated runs on commit `a524f46`, before the latest worker change, against 3 of 4 with it;
- they use the simulated secure-message channel, which never leaves an attempt `Submitted`.

A separate fix is tracked on `fix/escalation-test-race`.

**Post-merge gate flake.** The first full `scripts/test-all.ps1` run on `main` at `31447c0` (2026-10-08, PowerShell 7.6.6) failed one test: `EscalationProcessorTests.BackupDeliveryFailurePreservesTheDeliveredOriginalRecipientsResponsePath`, which passed 10 of 10 isolated runs. The escalation processor stamps the backup outbox row with PostgreSQL `clock_timestamp()`, but the dispatch claim compares `next_attempt_at_utc` with the host clock. The test container's clock measured between 150 ms behind and 105 ms ahead of the host, so the backup row can be not yet due and the claim returns `no-work`. A dispatch clock 500 ms behind the system clock reproduced the failure every run. The test now waits at most one second, only while the outcome is `no-work`; a 5-second skew still fails the assertion. Outside tests, the same split can delay an escalation dispatch by the worker's clock skew; it never skips or duplicates one. With this fix, the complete `scripts/test-all.ps1` gate passed on `fix/escalation-backup-dispatch-clock-flake` (backend 480/480, web 111/111, smoke 1/1, system E2E 12/12 and Phase 11 2/2, restore, and all three container builds). The smoke step needs `PLAYWRIGHT_BROWSERS_PATH` set to the repository's `.playwright-browsers` folder when the user-profile Playwright cache lacks the pinned Chromium.

The full `scripts/test-all.ps1` gate was not run. It requires PowerShell 7, and it also runs the browser system harnesses, restore and container builds. Phase 12 changes none of those, but they remain unverified on this branch.

## E2E evidence

`AcsSmsEndToEndTests` writes `TestResults/phase12/acs-sms-e2e-evidence.json`, which is ignored by git. It records:

- **Delivered closed loop:** 1 signed provider request, the attempt `Delivered`, 3 delivery events and the outbox `Processed`. Neither acknowledgement nor responsibility is inferred. Three polls while waiting produce no resend and a single retry audit record.
- **Webhook authentication:** no token, bad signature, wrong audience, wrong issuer, expired token and development cookie each get 401. A missing role gets 403.
- **Webhook validation:** a wrong content type gets 415 and oversize gets 413. Each of these gets 400 and nothing is applied:
  - 51 events in a batch;
  - an unsupported event type or wrong topic;
  - a stale or future event time;
  - an unknown status or unsafe message ID;
  - duplicate IDs in a batch;
  - malformed JSON;
  - an unintended subscription;
  - a handshake header on a report.
- **Subscription validation:** anonymous gets 401. The documented handshake shape for the configured subscription gets 200 and an echo of the code, for any topic and any case of the subscription name. An unintended or missing subscription name, a wrong `aeg-event-type`, a stale handshake or a missing ID gets 400.
- **Ambiguous outcome:** retried with a single repeatability ID; ends as one attempt.
- **Missing report:** fails as `delivery-unconfirmed`, the alert becomes `Failed`, and a `DeliveryFailed` warning is shown.
- **Report racing the worker commit:** deferred with 503, then applied on redelivery.
- **Directory change after acceptance:** the attempt keeps waiting and closes as `Delivered` when the report arrives, without resending. With no report, it fails as `delivery-unconfirmed` instead of staying pending.
- **SMS provider changed while waiting:** the attempt fails as `delivery-unconfirmed` and is never marked delivered by the other provider.
- **Crash after acceptance, before commit:** the replay sends the same repeatability ID and the same first-sent time (the ledger's first-send time), and keeps the original message ID.
- **First send after a 10-minute queue backlog:** `repeatabilityFirstSent` is the actual send time, and the send is accepted instead of getting a `412` from the fake's conservative 5-minute rule.
- **Recovery 3 minutes after a crash:** the attempt fails visibly as `provider-outcome-uncertain` with no second send.
- **Batched reports:** a webhook batch whose attempt the worker makes terminal between two reports re-reads the attempt. The worker's `delivery-unconfirmed` is not overwritten.
- **Provider selection:** Simulation is the default, and both Production SMS and the Production webhook refuse startup.
- **Lost accepted send, then a throttled replay:** three provider requests share one repeatability ID; one accepted SMS; one attempt recovers the original message ID.
- **Crash, then restart with the simulated provider:** the attempt keeps the ACS provider, fails as `delivery-unconfirmed`, and no simulated delivery event exists.
- **Backup delivered (accepted or unanswered), then the primary SMS times out:** the primary attempt and outbox are failed and a `DeliveryFailed` warning shows, but the alert stays Active and resolution returns 200.
- **Four report polls, then one transient worker fault:** rescheduled, not failed; a later report closes the outbox.
- **121 seconds pass before the first send:** the SMS is sent, with the send-boundary time as its first-send time.
- **Wrong Event Grid principal:** subscription-writer (v1 and v2), missing-sender and conflicting-sender tokens get 403 for reports and for the handshake. The configured sender is authorized.

Sentinel scans of database rows, captured logs and the artifact found no test number, access key, bearer token, SMS text or provider detail text.

## PR #9 review fixes

Each fix has a test that failed before the change:

- **F28 (race):** a delivery report racing the worker commit was acknowledged and lost. It is now deferred with `503` and `Retry-After` for 10 minutes.
- **F29 (wrong JSON shape):** a `202` body of valid JSON with the wrong shape threw a generic worker error. It is now an uncertain outcome; 11 shapes are covered.
- **F30 (handshake):** the handshake was echoed for any topic. It is now bound to the configured `SubscriptionName` through `aeg-subscription-name`, because the handshake's body topic is documented as a subscription path. A short-lived `ValidationTopic` check was replaced for that reason.
- **F31 (removed mapping):** a `Submitted` attempt was falsely failed when its test-recipient mapping was removed. Status is now handled before the mapping lookup.
- **F32 (stalled body):** a stalled `202` body could hold the worker transaction and alert lock indefinitely. One linked per-request timeout now bounds headers and body.
- **F33 (directory change):** a `Submitted` attempt was rescheduled forever after a directory change. It now bypasses directory checks, and an attempt whose provider is no longer configured fails visibly as unconfirmed.
- **F34 (first-sent time):** `repeatabilityFirstSent` came from the attempt row, which a crash can roll back. It briefly came from the outbox creation time; F35 replaced that.
- **F35 (P1, stale first-sent):** the outbox creation time can be minutes or hours old after a backlog. ACS documents `412` for a first-sent value outside its 5-minute tracking (for Email and Rooms; unverified for SMS), and that was mapped to `sms-rejected`, so the SMS would never be sent. Now:
  - the actual first-send time is committed to `provider_send_ledger` on its own connection before the network call;
  - replays are bounded from that time, with the window capped at 240 s;
  - `412` is ambiguous.
- **F36 (stale tracked attempt):** one webhook request reused a tracked attempt across batch items and could overwrite a terminal status set in between. Each report now starts from a clean change tracker.

## Comprehensive audit fixes (2026-10-07 audit of `ef24f1d`)

An external audit (`hospital-pr9-comprehensive-audit`) reported seven findings. It modeled the backend with scripts but did not run .NET or PostgreSQL. Each finding now has a real PostgreSQL E2E test (or, for F43, a web-boundary test) that failed against `ef24f1d`, and each fix is verified by it.

| Audit | Design | Fix |
| --- | --- | --- |
| F1 (P1) | F37 | `429` and item `429`/`5xx` no longer fail the attempt. The same repeatability ID and first-send time are retried, and no new key is minted after ambiguous history. |
| F2 | F38 | Before creating any attempt, the worker reads the send ledger for that key under every provider. A different provider's send is recorded as `delivery-unconfirmed` with its original provider and never dispatched. |
| F3 | F39 | The alert-level failure transition first checks backup attempts that were delivered or are in flight, queued backup dispatch, and accepted responsibility. The primary failure stays visible. |
| F4 | F40 | The webhook policy requires `appid`/`azp` to equal the configured `SenderApplicationId`; missing, foreign or conflicting senders are refused. |
| F5 | F41 | `outbox_messages.worker_failure_count` (migration `20261007032738_Phase12WorkerFailureBudget`) is the failure budget; lease claims no longer spend it. |
| F6 | F42 | Attempt creation and the ledger first-send time use a clock read at the per-recipient send boundary. |
| F7 (pre-existing) | F43 | The web audit vocabularies equal the backend `AuditSafety` sets, enforced by a test that reads the backend source. |

The audit's other items:

- **Stale safety evidence:** the traceability row "No external endpoint exists" is replaced with the webhook's evidence.
- **5-minute retention claim:** it is now qualified everywhere as documented by Microsoft for Email and Rooms only.
- **Conditional risks:** same-adapter configuration drift between send and replay, policy retirement while an attempt is `Submitted`, live Event Grid batching and limiter behavior, and multi-segment encoding of non-default templates are documented as residual. None is a demonstrated runtime defect, and none is fixed in code.

## Remaining gates (REQUIRES_HOSPITAL_DECISION or later slices)

- A fake-data staging run against a real ACS resource and Event Grid subscription. It must confirm:
  - the handshake headers;
  - the real Microsoft.EventGrid sender ID (and that subscription-writer tokens are rejected);
  - SMS repeatability behavior (replay acceptance, retention, `412`) against the real service, with a synchronized worker clock.
- Same-adapter configuration drift (ACS resource, sender or test-recipient map changed between a send and its replay) is not detected; a later slice should bind replays to an immutable operation identity.
- A handset check that no patient detail reaches SMS.
- Provider contract, subprocessors, residency, sender registration and opt-out handling.
- Approval of the DEMO windows.
- Real-recipient enablement.
- The voice adapter slice.

Proposed change description: `feat: add signed ACS SMS delivery-report webhook and closed-loop E2E evidence`.
