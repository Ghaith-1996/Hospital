# Phase 12 verification: slice 1, ACS SMS

Status: slice 1 is implemented and locally verified with fake transports and test tokens. The project owner's review is pending. No live Azure request was made. Voice is not started.

## Baseline and scope

- Branch: `feature/phase-12-real-communication-adapter`, from `main` at `9209b41`.
- Phase 11 was merged through PR #8 before this branch. Post-merge `main` was checked: backend 393/393 and web 38/38 passed, and typecheck was clean.
- Design and failure modes F1–F29: [the slice 1 design](specs/2026-09-29-phase-12-acs-sms-adapter-design.md).
- Architecture: [real communication adapters](../architecture/real-communication-adapters.md).

## Checks run on 2026-09-29

| Check | Result |
| --- | --- |
| `dotnet format --verify-no-changes` | Passed |
| Release build | Passed, 0 warnings / 0 errors |
| Domain / Application / Architecture tests | 40 / 55 / 6 passed |
| API integration tests, including 8 Phase 12 E2E scenarios and the OpenAPI contract comparison | 208 passed |
| Infrastructure tests, including 52 ACS contract tests | 143 of 144 passed; see the flaky test below |
| Web unit tests / typecheck / lint | 38 passed / clean / clean |
| `verify-no-sensitive-data.ps1`, `verify-observability-safety.ps1` | Passed |
| Web storage-safety scan | Passed; its patterns were run manually because the script requires PowerShell 7, which is not installed here |
| `verify-openapi.ps1` | Not run (needs PowerShell 7). The runtime comparison in `OpenApiContractTests` passed, and the webhook is excluded from the client contract. |

The one Infrastructure failure is `EscalationProcessorTests.DueWorkerWaitsForConcurrentAcceptanceAndThenStopsWithoutActivation`. It is flaky before Phase 12: it failed on untouched `main` in 3 of 3 full-suite runs and passes alone. A separate fix is in progress on `fix/escalation-test-race`.

The full `scripts/test-all.ps1` gate was not run: it requires PowerShell 7, and it also runs the browser system harnesses, restore and container builds. Phase 12 changes none of those, but they remain unverified on this branch.

## E2E evidence

`AcsSmsEndToEndTests` writes `TestResults/phase12/acs-sms-e2e-evidence.json`, which is ignored by git. It records:

- Delivered closed loop: 1 signed provider request, the attempt `Delivered`, 3 delivery events, the outbox `Processed`, and neither acknowledgement nor responsibility inferred. Three polls while waiting produce no resend and a single retry audit record.
- Webhook authentication: no token, bad signature, wrong audience, wrong issuer, expired token and development cookie each get 401. A missing role gets 403.
- Webhook validation: wrong content type 415; oversize 413; 51 events, unsupported type, wrong topic, stale or future time, unknown status, unsafe message ID, duplicate IDs in a batch and malformed JSON each get 400. Nothing is applied.
- Subscription validation: anonymous gets 401; authenticated gets 200 and an echo of the code.
- An ambiguous outcome is retried with a single repeatability ID and ends as one attempt.
- A missing report fails as `delivery-unconfirmed`, the alert becomes `Failed`, and a `DeliveryFailed` warning is shown.
- Provider selection: Simulation is the default, and both Production SMS and the Production webhook refuse startup.

Sentinel scans of database rows, captured logs and the artifact found no test number, access key, bearer token, SMS text or provider detail text.

## PR #9 review fixes

Two automated review findings (P2) were fixed. Each has a test that failed before the fix:

- **F28:** a delivery report racing the worker commit was acknowledged and lost. It is now deferred with `503` and `Retry-After` for 10 minutes, then applied on redelivery. The E2E test records early `503`, redelivery `200` and final `Delivered`.
- **F29:** a `202` body that was valid JSON of the wrong shape threw a generic worker error. Every field kind is now validated, and any mismatch is an uncertain outcome retried with the same key. The contract test covers 11 shapes; 7 failed before the fix.

## Remaining gates (REQUIRES_HOSPITAL_DECISION or later slices)

- A fake-data staging run against a real ACS resource and Event Grid subscription.
- A handset check that no patient detail reaches SMS.
- Provider contract, subprocessors, residency, sender registration and opt-out handling.
- Approval of the DEMO windows.
- Real-recipient enablement.
- The voice adapter slice.

Proposed change description: `feat: add signed ACS SMS delivery-report webhook and closed-loop E2E evidence`.
