# Phase 12 verification: slice 2, provider-neutral voice

Status: implemented and locally verified on `feature/phase-12-slice-2-voice` with the test-only reference provider and RS256-signed test callbacks. No real voice provider exists or was called. The owner's review of the slice is pending. Design: [slice 2 design](specs/2026-10-08-phase-12-slice-2-provider-neutral-voice-design.md). Architecture: [real communication adapters](../architecture/real-communication-adapters.md#provider-neutral-voice-slice-2).

## Complete local gate

`scripts/test-all.ps1` passed on 2026-10-08 (PowerShell 7.6.6, Docker 29.1.3, `PLAYWRIGHT_BROWSERS_PATH` set to the repository `.playwright-browsers`):

| Check | Result |
| --- | --- |
| Sensitive-data, web-storage, observability and OpenAPI checks | Passed |
| `dotnet format --verify-no-changes`, vulnerable packages, Release build | Passed (0 warnings) |
| Backend tests | 516 passed: Domain 40, Application 55, Infrastructure 153, API integration 262 (36 voice E2E), Architecture 6 |
| AI evaluation | Passed |
| Web unit tests, typecheck, lint, production build | 147 passed (36 new failure-vocabulary cases); passed |
| Standalone browser smoke | 1 passed |
| Connected system E2E, Phase 11 assistance | 12 passed, 2 passed; teardown left no containers or processes |
| Real PostgreSQL restore and restore safety guards | Passed |
| API, worker and web container builds; web-to-API forwarding | Passed |

The branch includes the test-only clock-skew fix from PR #14 (`df45fe3`); without it, the known `EscalationProcessorTests` flake failed one run.

## Failure-mode evidence

`tests/CriticalAlerts.Api.IntegrationTests/VoiceEndToEndTests.cs` (36 tests) covers V1–V32 against the API host, real PostgreSQL and the real outbox processor, and writes `TestResults/phase12/voice-e2e-evidence.json` (ignored by git): scenario outcomes, attempt statuses and categories, and the reference provider's create-request, executed-call and signature counts. The fixture refuses to write the artifact if it contains a test number, caller ID, shared key, provider text, spoken text or any issued token; the same values are scanned for in database rows and captured logs. `src/web/tests/live-failure-vocabulary-contract.test.tsx` covers V31.

Two test errors were found and corrected during implementation, without changing production behavior: the crash tests first advanced the worker clock 30 s, inside the crashed worker's one-minute outbox lease (now 61 s, as in slice 1), and new C# files needed CRLF line endings for `dotnet format`.

## Remaining gates (REQUIRES_HOSPITAL_DECISION or later slices)

- The real voice provider, given the ACS retirement; and now the SMS provider too.
- Whether a voicemail greeting that plays the full message counts as delivered, and whether voicemail detection is required.
- Spoken wording, language, voice and repeats; ring timeout and the DEMO windows.
- Caller-ID procurement and registration; keypad or speech acknowledgement (needs its own safety review).
- A fake-data staging run against the chosen provider, a handset check that no patient detail is spoken, provider contract, residency, subprocessors and retention.
