# Phase 12 verification: slice 1, ACS SMS

Status: slice 1 is implemented and locally verified with fake transports and test tokens. The project owner's review is pending. No live Azure request was made. Voice is not started.

## Baseline and scope

- Branch: `feature/phase-12-real-communication-adapter`, from `main` at `9209b41`. It was later merged with `main` at `32641a3`, which brought in cleanup PRs #10 and #11.
- Phase 11 was merged through PR #8 before this branch. Post-merge `main` was checked: backend 393/393 and web 38/38 passed, and typecheck was clean.
- Design and failure modes F1–F36: [the slice 1 design](specs/2026-09-29-phase-12-acs-sms-adapter-design.md).
- Architecture: [real communication adapters](../architecture/real-communication-adapters.md).

## Latest checks (2026-10-06, after the F35/F36 fixes)

| Check | Result |
| --- | --- |
| `dotnet format --verify-no-changes` | Passed |
| Release build | Passed, 0 warnings / 0 errors |
| Domain / Application / Architecture tests | 40 / 55 / 6 passed |
| API integration tests, including 15 Phase 12 E2E scenarios and the OpenAPI contract comparison | 213 passed |
| Infrastructure tests, including 61 ACS contract tests | 152 of 153 passed; the only failure is a pre-existing escalation flake (see below) |
| Web unit tests / typecheck / lint (last run after the `main` merge; no web change since) | 38 passed / clean / clean |
| `verify-no-sensitive-data.ps1`, `verify-observability-safety.ps1` | Passed (2026-09-29) |
| Web storage-safety scan | Passed; its patterns were run manually because the script requires PowerShell 7, which is not installed here |
| `verify-openapi.ps1` | Not run (needs PowerShell 7). The runtime comparison in `OpenApiContractTests` passed, and the webhook is excluded from the client contract. |

**Escalation flakes.** Intermittent failures occur in `EscalationProcessorTests`: `DueWorkerWaitsForConcurrentAcceptanceAndThenStopsWithoutActivation` and `QueuedBackupDispatchRechecksDurableStopConditions`. They predate these changes:

- they fail on untouched `main`;
- they failed in 2 of 4 isolated runs on commit `a524f46`, before the latest worker change, against 3 of 4 with it;
- they use the simulated secure-message channel, which never leaves an attempt `Submitted`.

A separate fix is tracked on `fix/escalation-test-race`.

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
- **First send after a 10-minute queue backlog:** `repeatabilityFirstSent` is the actual send time, and the send is accepted instead of getting a 412 rejection.
- **Recovery 3 minutes after a crash:** the attempt fails visibly as `provider-outcome-uncertain` with no second send.
- **Batched reports:** a webhook batch whose attempt the worker makes terminal between two reports re-reads the attempt. The worker's `delivery-unconfirmed` is not overwritten.
- **Provider selection:** Simulation is the default, and both Production SMS and the Production webhook refuse startup.

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
- **F35 (P1, stale first-sent):** the outbox creation time can be minutes or hours old after a backlog, and ACS answers a first-sent value outside its 5-minute tracking with `412`, which was mapped to `sms-rejected`, so the SMS was never sent. Now:
  - the actual first-send time is committed to `provider_send_ledger` on its own connection before the network call;
  - replays are bounded from that time, with the window capped at 240 s;
  - `412` is ambiguous.
- **F36 (stale tracked attempt):** one webhook request reused a tracked attempt across batch items and could overwrite a terminal status set in between. Each report now starts from a clean change tracker.

## Remaining gates (REQUIRES_HOSPITAL_DECISION or later slices)

- A fake-data staging run against a real ACS resource and Event Grid subscription. It must also confirm the handshake headers and ACS repeatability behavior (replay acceptance, `412` outside 5 minutes) against the real service, with a synchronized worker clock.
- A handset check that no patient detail reaches SMS.
- Provider contract, subprocessors, residency, sender registration and opt-out handling.
- Approval of the DEMO windows.
- Real-recipient enablement.
- The voice adapter slice.

Proposed change description: `feat: add signed ACS SMS delivery-report webhook and closed-loop E2E evidence`.
