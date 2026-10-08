# Phase 12 — Slice 2: provider-neutral voice adapter

Authorized on 2026-10-08 by the project owner's instruction to start Phase 12 slice 2, and the owner's choice of a provider-neutral voice boundary over an ACS Call Automation adapter. Baseline: `main` at `31447c0` (PR #9, slice 1 ACS SMS, merged; full local gate passed with the test-only fix in PR #14). Branch: `feature/phase-12-slice-2-voice`.

Status: **design only, for owner review.** No code is written until this design is approved.

## Why provider-neutral

The slice 1 design named the ACS voice/call-automation adapter as the next slice. Microsoft's retirement guide (https://learn.microsoft.com/en-us/azure/communication-services/acs-retirement-and-breaking-changes-guide, updated 2026-09-24, checked 2026-10-08) changes that:

- ACS is retired as a standalone offering on **September 30, 2028**, announced September 2026.
- From **October 23, 2026**, new customers cannot sign up for retiring ACS services. Only resources created before that date continue.
- ACS SMS and ACS PSTN (Direct Offer) are **retired**. Call Automation is a **breaking change**: after September 30, 2028 it is supported only with Teams-aligned services such as Teams Phone Extensibility.
- Microsoft points SMS and PSTN customers to Marketplace partners (for example Infobip and Telesign).

This project has never created an ACS resource. The voice provider is therefore `REQUIRES_HOSPITAL_DECISION`, and this slice builds the boundary that any approved provider plugs into. The slice 1 ACS SMS adapter is unchanged, but its provider choice is now also an open decision (see [Open decisions](#open-decisions-requires_hospital_decision)).

## Scope

In scope:

- A generic `ProviderVoiceChannel` that implements the existing `INotificationChannel` port for `Voice`, behind the transactional outbox. It owns validation, test-number mapping, the durable first-send ledger, the time windows and the state mapping. A concrete provider supplies only two small ports.
- **Test-number-only** dialing. Directory contact values are never read, decrypted or dialed.
- A generic, authenticated call-event webhook at `POST /api/v1/webhooks/communications/voice/{provider}`.
- A closed, provider-neutral call-event vocabulary and its mapping to the existing `Submitted` / `Delivered` / `Failed` attempt dimensions.
- A **test-only reference provider**: an HTTP fake that verifies requests, executes the call script, and posts signed callbacks to the real API endpoint. It lives in the test projects and is never registered by the production host.
- One E2E backend flow test that writes a repeatable evidence artifact.

Out of scope, all `REQUIRES_HOSPITAL_DECISION` or a later slice:

- Any concrete real voice provider (ACS, a Marketplace partner or another CPaaS).
- Calling real directory numbers, production enablement, caller-ID procurement, contracts, residency and subprocessors.
- Acknowledging or accepting responsibility by keypad (DTMF) or speech. A call never records a response.
- Call recording, transcription, inbound calls, voicemail detection and voicemail-specific wording.
- Any change to recipient selection, confirmation, escalation or responsibility.
- Consolidating the slice 1 SMS report service into the new pipeline.

## Authority

The adapter is a transport, exactly as in slice 1. It cannot select recipients, change the approved message, dispatch without a confirmed outbox row, stop escalation or record a response. The spoken text is the approved notification policy's `GenericVoiceTemplate`, which already requires the `SIMULATION:` prefix and holds no alert, patient, clinical, recipient or location data. Full case content stays in the authenticated interface.

A provider accepting the call request is recorded only as `Submitted`. Only an authenticated call event proving the message finished playing can produce `Delivered`. Delivered never implies opened, acknowledged or responsibility accepted, and voice attempts keep `OpenedState = NotApplicable`.

## What "Delivered" means for a call

| Evidence | Attempt state | Meaning |
| --- | --- | --- |
| Provider accepted the call request (call ID returned) | `Submitted` | Call requested only |
| `answered` event | stays `Submitted` | Something picked up. Recorded as a `call-answered` delivery event, never shown as delivered |
| `playback-completed` event | `Delivered` | The whole wake-up message played on an answered call. A person and a voicemail greeting cannot be told apart; see open decisions |
| `ended` with `no-answer` before playback completed | `Failed` `voice-no-answer` | Existing category, now produced by a real boundary |
| `ended` with `busy` | `Failed` `voice-busy` | |
| `ended` with `declined` | `Failed` `voice-declined` | |
| `ended` with `unreachable` or `failed` | `Failed` `voice-call-failed` | |
| `ended` with `hung-up` after `answered`, before playback completed | `Failed` `voice-playback-incomplete` | Partial playback is not delivery |
| `playback-failed` | `Failed` `voice-playback-failed` | |
| No terminal event within `CallOutcomeWindowSeconds` (DEMO 180) | `Failed` `call-outcome-unconfirmed` | Visible; the call is never placed again |
| Endpoint label without a test mapping | `Failed` `test-recipient-not-configured` | No network call (existing category) |

Terminal states never regress. `ended` after `Delivered` is recorded as `no-state-change`. A late `playback-completed` after `call-outcome-unconfirmed` does not reopen the failure.

## Architecture

```text
exact human confirmation ──> identifier-only outbox row
                                   │
                    OutboxDispatchProcessor (lease, alert lock)
                                   │  INotificationChannel (Voice)
               ┌───────────────────┴──────────────────┐
     SimulationVoiceChannel (default)      ProviderVoiceChannel (generic)
                                           │ validate text, map SIM-VOICE label → test number
                                           │ ledger: first-send time + operation fingerprint (own connection)
                                           │ IVoiceCallProvider.CreateCallAsync(script, tag, repeat key)
                                           ▼
                              provider ──accepted──> attempt Submitted (call ID = provider reference)
                                           │
                     signed callback  POST /api/v1/webhooks/communications/voice/{provider}
                                           │ IVoiceCallbackReader: authenticate, then parse to closed vocabulary
                                           ▼
       ProviderCallEventService: attempt lookup ─ alert lock ─ inbox/event dedupe ─ tag check ─ state mapping
                                           ▼
                attempt Delivered / Failed(voice-*), delivery event, audit, inbox
```

### Ports a concrete provider implements

```csharp
public interface IVoiceCallProvider
{
    string Name { get; }                       // closed, configured provider name, e.g. "reference-fake"
    bool SupportsIdempotentCreate { get; }     // true only if the provider documents request repeatability
    Task<VoiceCallCreateResult> CreateCallAsync(VoiceCallRequest request, CancellationToken ct);
}

public sealed record VoiceCallRequest(
    string ToTestNumber, string SpokenText, int Repeats, int RingTimeoutSeconds,
    string Tag, Guid RepeatabilityId, DateTimeOffset FirstSentAtUtc, Uri CallbackUri);

// Accepted(callId) | Rejected(category) | AuthFailed | Unavailable(retryAt?) | Ambiguous
public abstract record VoiceCallCreateResult;

public interface IVoiceCallbackReader
{
    string Name { get; }
    Task<VoiceCallbackAuthentication> AuthenticateAsync(HttpRequest request, ReadOnlyMemory<byte> body, CancellationToken ct);
    IReadOnlyList<VoiceCallEvent> Parse(ReadOnlyMemory<byte> body);   // throws on any invalid event
}

public sealed record VoiceCallEvent(
    string EventId, string CallId, string? Tag, VoiceCallEventKind Kind, VoiceCallEndReason? EndReason,
    DateTimeOffset OccurredAtUtc);
```

The call script is provider-executed: speak `SpokenText` `Repeats` times, then hang up. A provider that needs mid-call commands to do this (for example a "play" request after the call connects) issues them inside its own adapter. Those commands must be idempotent and must never create a second call.

### Callback authentication

Each provider's reader authenticates before parsing anything. The generic endpoint does not trust any provider-specific header by itself. The reference fake signs each callback with an RS256 JWT published through a JWKS, with a fixed issuer, the configured audience and a 5-minute lifetime. This mirrors the OIDC-style callback tokens used by ACS Call Automation (https://learn.microsoft.com/en-us/azure/communication-services/how-tos/call-automation/secure-webhook-endpoint, checked 2026-10-08), so a later ACS or similar adapter fits without changing the pipeline. A provider that signs with a shared-secret HMAC fits the same port.

Because such tokens may not be bound to the body, authenticity alone is not enough. Every event is also bound to its attempt by call ID **and** tag, and deduplicated by event ID.

### Durable ledger and operation fingerprint

Before every network call, the channel writes the ledger row on its own committed connection, as slice 1 does. Voice adds an `operation_fingerprint` column: SHA-256 of the provider name, the configured provider account identity, the caller ID, the mapped test number and the spoken text. A replay whose fingerprint differs from the ledger row (configuration changed between send and replay) is **never** sent. It fails visibly as `provider-outcome-uncertain`. This closes, for voice, the same-adapter drift risk that slice 1 left documented.

### Ambiguous outcomes

- **Provider supports idempotent create:** an ambiguous outcome (timeout, transport error after send began, 5xx/408, unreadable reply, redirect) leaves the attempt `Requested`. The same repeatability ID and first-send time are retried until `UncertainOutcomeWindowSeconds` (DEMO 120, range 10–240), then the attempt fails visibly as `provider-outcome-uncertain`.
- **Provider does not:** an ambiguous outcome is never retried, because a second call at night is a duplicate dispatch. The attempt fails immediately and visibly as `provider-outcome-uncertain`, and manual fallback is shown.
- Definite non-acceptance (for example 429 with no call created) uses the existing bounded `provider-unavailable` retry with a new attempt number, only when the provider documents that the request was not executed.

### Events that arrive before the send commits

A call can be answered seconds after creation, before the worker commits the call ID. Slice 1 relied on Event Grid redelivery (`503`). Voice callback senders may not redeliver, so the voice pipeline does not rely on it. An authenticated event whose call ID is unknown but whose tag matches a committed ledger row is stored in the inbox as `pending-attempt`. The worker applies pending events, in `OccurredAtUtc` order, when it next processes that attempt, and the call ID is checked then. An event whose tag matches no ledger row is accepted and dropped, with nothing stored.

### Configuration (fail closed at startup)

```text
Communications:Voice:Provider = Simulation | <registered provider name>
Communications:Voice:CallerId = <E.164 test caller ID>
Communications:Voice:TestRecipients:<SIM-VOICE-label> = <approved E.164 test number>
Communications:Voice:Repeats = 2                         (DEMO, 1–3)
Communications:Voice:RingTimeoutSeconds = 30             (DEMO, 10–60)
Communications:Voice:CallOutcomeWindowSeconds = 180      (DEMO, 60–1800)
Communications:Voice:UncertainOutcomeWindowSeconds = 120 (DEMO, 10–240)
Communications:Webhooks:Voice:Enabled = false
Communications:Webhooks:Voice:PublicBaseUri = https://<host>     (callback base; https only)
```

- `Simulation` remains the default. Any unregistered provider name refuses startup.
- `Production` refuses startup for any non-simulation provider and for the webhook. The dispatch worker still runs only in Development/Test until a staging host exists.
- `TestRecipients` must be non-empty and every value E.164. Provider secrets come only from ignored local configuration, user secrets, environment variables or Key Vault.
- All windows and counts are technical DEMO values, not clinical or production service levels.

### Webhook limits

`application/json` only, at most 64 KiB, at most 50 events per request, and the existing address-partitioned fixed-window rate limit. Each event must have a safe ID of at most 100 characters, a safe call ID, a kind from the closed vocabulary, and an `OccurredAtUtc` no older than 48 hours and no more than 5 minutes in the future. Phone numbers, caller names, provider free text and any transcript-like field are never read into the model, persisted, logged or audited. Development cookies and every other authentication scheme are refused on this route. The route is only mapped when the webhook is enabled, a non-simulation provider is configured, and the environment is not Production. An unknown `{provider}` segment returns `404`.

## Failure modes (written before the code)

"Isolated" means a fake-transport contract test. "E2E" means the API host, a real PostgreSQL container, the real outbox processor and the reference fake provider over HTTP.

| # | Failure | Required behavior | Test |
| --- | --- | --- | --- |
| V1 | Provider left at default | Simulation voice channel used, no network, webhook route absent | E2E |
| V2 | Non-simulation provider configured in Production, or webhook enabled in Production | Startup refused | isolated |
| V3 | Unregistered provider name, empty `TestRecipients`, non-E.164 caller ID or test number, window or count out of range, non-https callback base | Startup refused; no secret echoed | isolated |
| V4 | Endpoint label not in `TestRecipients` | `Failed` `test-recipient-not-configured`, zero provider calls | E2E |
| V5 | Spoken text lacks `SIMULATION:`, is over 200 characters or has control characters | `DispatchValidationException`, zero provider calls | isolated |
| V6 | Provider accepts the call | `Submitted` only, call ID stored as the provider reference, never `Delivered` | E2E |
| V7 | `answered`, then `playback-completed` | `call-answered` event with no state change, then `Delivered`; live projection shows delivered but not acknowledged or accepted | E2E |
| V8 | `ended` `no-answer` / `busy` / `declined` / `unreachable` / `failed` before playback | `Failed` with the matching `voice-*` category, visible warning, escalation and backup behavior unchanged | E2E |
| V9 | `answered` then `ended` `hung-up` before playback completed | `Failed` `voice-playback-incomplete`, never `Delivered` | E2E |
| V10 | `playback-failed` | `Failed` `voice-playback-failed` | E2E |
| V11 | No terminal event within `CallOutcomeWindowSeconds` | `Failed` `call-outcome-unconfirmed`, no second call | E2E |
| V12 | Duplicate event (same event ID), including redelivery after success | No second delivery event, no state change, `200` | E2E |
| V13 | Out-of-order: `ended` `no-answer` after `Delivered`; `playback-completed` after `call-outcome-unconfirmed` | No regression; recorded as `no-state-change` | E2E |
| V14 | Callback without token, bad signature, wrong issuer or audience, expired, unknown key, development cookie | `401`/`403`, nothing stored | E2E |
| V15 | Wrong content type, oversize, more than 50 events, unknown kind or end reason, unsafe IDs, stale or future event | `4xx`, nothing stored, whole request rejected | E2E |
| V16 | Authenticated event with a valid call ID but a tag for another attempt | Rejected in the inbox, no state change | E2E |
| V17 | Event for an unknown call ID whose tag matches a committed ledger row (event beats the send commit) | Stored as `pending-attempt`; applied in order once the attempt commits; final state equals the in-order outcome | E2E |
| V18 | Event whose call ID and tag match nothing | `200`, nothing stored | E2E |
| V19 | Provider supporting idempotent create: timeout, transport error, 5xx/408, unreadable or wrong-shape reply, redirect | Stays `Requested`; retries reuse the same repeatability ID and first-send time; after the window `provider-outcome-uncertain`; the fake executes at most one call | E2E + isolated |
| V20 | Provider without idempotent create: any ambiguous outcome | Immediately `Failed` `provider-outcome-uncertain`, never retried; the fake records exactly one create request | E2E |
| V21 | Worker crashes after the provider accepted the call but before the dispatch transaction commits | The recreated attempt replays with the same repeatability ID and first-send time (idempotent provider: original call ID recovered), or fails as `provider-outcome-uncertain` without calling (non-idempotent provider). Never two calls | E2E |
| V22 | Configuration changes between a send and its replay (provider account, caller ID, test-number mapping or spoken text) | Fingerprint mismatch: no replay, `Failed` `provider-outcome-uncertain` | E2E |
| V23 | Worker restarts with a different voice provider, or the default simulation, after a provider already sent the attempt | The ledger keeps the original provider; the recreated attempt fails as `delivery-unconfirmed` and is never dispatched; simulated delivery is never substituted | E2E |
| V24 | A `Submitted` attempt's directory endpoint, practitioner or role changes, or the test mapping is removed | The attempt keeps waiting for its events with the provider that placed the call, no lookup, no new call; only the outcome window fails it | E2E |
| V25 | Provider auth failure (401/403) on create | `Failed` `provider-auth-failed`, not retried | isolated |
| V26 | Provider definitively rejects the request (4xx other than 408/429) | `Failed` `voice-call-rejected`, not retried | isolated |
| V27 | Provider documents non-execution with 429 | Bounded `provider-unavailable` retry with a new attempt number | isolated |
| V28 | Provider reply streams slowly after headers | One linked timeout covers headers and body; expiry is ambiguous (V19/V20 rules); worker shutdown still propagates | isolated |
| V29 | Phone numbers, caller ID, secrets, tokens, spoken text in database rows, logs, metrics, audit, problem details or the evidence artifact | Absent (sentinel scan) | E2E |
| V30 | The primary voice attempt fails after approved backup work was delivered, is pending or holds responsibility | Same as slice 1 F39: primary failure visible, alert stays workable, escalation continues | E2E |
| V31 | Web live view receives a new `voice-*` or `call-outcome-unconfirmed` category | Backend projection and web vocabulary list it; it renders as a visible failure; unknown values are still rejected | E2E (web boundary) |
| V32 | Unknown `{provider}` path segment, or a valid reader called for a provider other than the configured one | `404`, nothing stored | E2E |

## Evidence artifact

The E2E test writes `TestResults/phase12/voice-e2e-evidence.json`, ignored by git. It contains scenario names and outcomes, attempt statuses and failure categories, event, inbox and audit counts, and the reference fake's create-request count, executed-call count and request-verification results. It never contains numbers, caller IDs, keys, tokens or spoken text. The test is deterministic: fixed fictional data, the reference fake and a controllable clock.

## Data changes

- `provider_send_ledger.operation_fingerprint` (nullable for existing SMS rows; required for voice rows).
- No new tables. Pending events reuse `inbox_messages` with the `pending-attempt` outcome and a handler per provider.
- New failure categories added to `AlertLiveQueryService` and `src/web/lib/alerts.ts`: `voice-busy`, `voice-declined`, `voice-call-failed`, `voice-playback-incomplete`, `voice-playback-failed`, `voice-call-rejected`, `call-outcome-unconfirmed`.

## Open decisions (REQUIRES_HOSPITAL_DECISION)

- **The voice provider**, given the ACS retirement and the October 23, 2026 sign-up cutoff. The same question now applies to slice 1's ACS SMS.
- Whether a voicemail greeting that plays the full message counts as delivered, and whether voicemail detection is required.
- The approved spoken wording, language and voice (jurisdiction is itself undecided), and the number of repeats.
- Ring timeout and the call-outcome and uncertain windows.
- Caller-ID procurement and registration, and call-labeling or spam-flagging handling.
- Keypad or speech acknowledgement, which would change the response model and needs its own safety review.
- Recording and transcription policy (none in this slice).
- Real-recipient enablement, the provider contract, subprocessors, residency and retention of call IDs.
- Staging deployment and the callback host.
