# Real communication adapters (Phase 12)

Status: slice 1, Azure Communication Services (ACS) SMS, is implemented and locally verified with fake transports only. No live ACS request has been made. Voice is a provider-neutral boundary with no real provider yet (slice 2); pager and email are not implemented. The design and failure modes are in [the Phase 12 slice 1 design](../superpowers/specs/2026-09-29-phase-12-acs-sms-adapter-design.md).

Microsoft is retiring standalone ACS: new customers are blocked from 2026-10-23, and ACS SMS and PSTN are retired on 2028-09-30 (https://learn.microsoft.com/en-us/azure/communication-services/acs-retirement-and-breaking-changes-guide, checked 2026-10-08). The SMS provider choice is therefore `REQUIRES_HOSPITAL_DECISION` again. Slice 2 is a provider-neutral voice boundary, implemented with a test-only reference provider (see [below](#provider-neutral-voice-slice-2)): [slice 2 design](../superpowers/specs/2026-10-08-phase-12-slice-2-provider-neutral-voice-design.md).

## Where the adapter sits

```text
exact human confirmation ──> identifier-only outbox row
                                   │
                    OutboxDispatchProcessor (lease, alert lock)
                                   │  INotificationChannel (Sms)
               ┌───────────────────┴──────────────────┐
     SimulationSmsChannel (default)     AzureCommunicationServicesSmsChannel
                                        │  HMAC-signed POST /sms?api-version=2021-03-07
                                        │  to = TestRecipients[SIM label]   (never a directory value)
                                        │  message = policy generic SMS template
                                        ▼
                                  ACS ──202──> attempt Submitted (messageId stored as provider reference)
                                        │
                              Event Grid (Entra bearer token)
                                        ▼
            POST /api/v1/webhooks/communications/acs-sms  (JWT scheme EventGridWebhook only)
                                        │  validate whole batch; drop from/to/subject/details
                                        ▼
       ProviderDeliveryReportService: attempt lookup ─ alert lock ─ inbox/event dedupe ─ tag check
                                        ▼
                     attempt Delivered / Failed(sms-delivery-failed), event, audit, inbox
                                        ▼
                     next worker pass completes the outbox or fails it visibly
```

## State semantics

| Evidence | Attempt state | Meaning |
| --- | --- | --- |
| ACS `202` item `successful` with message ID | `Submitted` | Accepted by provider only |
| Authenticated report `Delivered` | `Delivered` | Handset delivery reported; not opened, acknowledged or accepted |
| Authenticated report `Failed` | `Failed` `sms-delivery-failed` | Visible failure; manual fallback |
| No report within `DeliveryReportWindowSeconds` (DEMO 300) | `Failed` `delivery-unconfirmed` | Visible failure; the SMS is never resent |
| Timeout, transport error, overall 5xx/408, unreadable reply | stays `Requested` | Same repeatability key retried until `UncertainOutcomeWindowSeconds` (DEMO 120), then `provider-outcome-uncertain` |
| Overall 429, item 429/5xx without message ID | stays `Requested` | Same repeatability ID retried until the uncertain window, then `provider-outcome-uncertain`. A refused replay does not prove an earlier invocation was never accepted, so throttling never creates a new attempt or key |
| Overall/item 4xx | `Failed` `sms-rejected` | Not retried |
| Overall 401/403 | `Failed` `provider-auth-failed` | Not retried; configuration incident |
| Repeatability `rejected` | `Failed` `provider-repeatability-rejected` | Not retried |
| Endpoint label without a test mapping | `Failed` `test-recipient-not-configured` | No network call |

An unmatched report less than 10 minutes old gets `503` with `Retry-After`, and nothing is stored. It may have raced the worker transaction that stores the message ID, so Event Grid redelivers it. An unmatched report older than that is accepted and dropped. A `202` body of the wrong JSON shape is treated as an ambiguous outcome and retried with the same key.

Once ACS has accepted a send (`Submitted`), the attempt only waits for its report.
- **No re-checks:** it skips directory and policy checks, so a removed endpoint, inactive practitioner or invalid role after acceptance cannot fail it, resend it or leave it pending forever.
- **Same provider only:** it is never evaluated by a different SMS provider. If the provider that sent a `Requested` or `Submitted` attempt is no longer configured, the attempt fails visibly as `delivery-unconfirmed`.
- **Provenance survives rollbacks:** before creating any attempt, the worker reads the send ledger for that attempt key under every provider. If a different provider already sent it (a crash rolled back the attempt row, then the worker restarted with another or the default Simulation provider), the recreated attempt keeps the original provider, fails as `delivery-unconfirmed` and is never dispatched.
- **Send-boundary clock:** the attempt and its ledger first-send time use a clock value read right before that recipient's send, after the claim, the alert lock and earlier recipients.
- **Failure budget:** report polls reclaim the outbox row but are not failures. Only unexpected worker exceptions count against `MaxAttempts` (`outbox_messages.worker_failure_count`).
- **Backup work keeps the alert workable:** when every original recipient has failed (for example an SMS report timeout), the original outbox row fails and the failure stays visible. The alert is not marked `Failed`, and escalation is not stopped, while approved backup work was delivered, is in flight or queued, or holds accepted responsibility. Without backup work the approved Phase 9 behavior is unchanged: the alert fails and manual fallback is shown.
- **Bounded request:** one 10-second timeout covers the response headers and the streamed body. If it expires, the outcome is ambiguous: the attempt stays `Requested` and is retried with the same key.
- **Replay-stable request ID:** `repeatabilityFirstSent` is the time of the attempt's first actual send. It is committed to `provider_send_ledger` on its own connection immediately before the network call. If the worker dies after ACS accepted a send, the recreated attempt replays the identical repeatable request and gets back the original message ID.
- **Bounded replays:** same-key replays are measured from that durable time and capped at 240 s. A later recovery fails visibly as `provider-outcome-uncertain` without sending. A `412` (first-sent outside repeatability tracking) is treated as ambiguous, never as `sms-rejected`. Microsoft documents 5-minute repeatable-request tracking and `412` for Email and Rooms (https://learn.microsoft.com/en-us/rest/api/communication/repeatable-requests, checked 2026-10-06). SMS carries repeatability per recipient in the body, and its retention is **not documented or verified**, so the bound is a conservative technical choice that the synthetic staging gate must confirm.
- **Fresh state per report:** each delivery report in a webhook batch starts from a clean change tracker and re-reads the attempt after taking the alert lock. A terminal status the worker or another webhook set in between is never overwritten.

Terminal states never regress. A late `Failed` report after `Delivered` is recorded as `no-state-change`. A late `Delivered` after `delivery-unconfirmed` does not reopen the failure. SMS attempts keep `OpenedState = NotApplicable`.

## Configuration

Keys are listed below. The secret values come only from ignored local configuration, user secrets, environment variables or Key Vault.

```text
Communications:Sms:Provider = Simulation | AzureCommunicationServices
Communications:Sms:AzureCommunicationServices:Endpoint = https://<resource>.communication.azure.com
Communications:Sms:AzureCommunicationServices:AccessKey = <secret>
Communications:Sms:AzureCommunicationServices:FromNumber = <E.164 sender>
Communications:Sms:AzureCommunicationServices:TestRecipients:<SIM-SMS-label> = <approved E.164 test number>
Communications:Sms:AzureCommunicationServices:DeliveryReportWindowSeconds = 300   (DEMO)
Communications:Sms:AzureCommunicationServices:UncertainOutcomeWindowSeconds = 120 (DEMO)
Communications:Webhooks:EventGrid:Enabled = false
Communications:Webhooks:EventGrid:TenantId / Audience / ExpectedTopic (ACS resource ID) / SubscriptionName (Event Grid event subscription name)
Communications:Webhooks:EventGrid:SenderApplicationId = <application ID of the Microsoft.EventGrid service principal in this tenant's cloud>
```

The worker validates the SMS settings at startup, even when dispatch is disabled. The API validates the webhook settings at startup, including a nonzero GUID for `SenderApplicationId`, and retains its canonical GUID value for matching token claims. Production refuses both.

Event Grid setup:

- The subscription must use the **Event Grid schema**. The CloudEvents OPTIONS handshake is not implemented.
- The subscription must filter to `Microsoft.Communication.SMSDeliveryReportReceived`.
- Its name must equal `SubscriptionName`. Every delivery is checked against the `aeg-subscription-name` header, case-insensitively, and against an `aeg-event-type` that matches the body. The handshake's body `topic` is not compared, because Event Grid documents it as a subscription path rather than the event source.
- Microsoft Entra authentication must be enabled with the webhook app registration, which grants the `AzureEventGridSecureWebhookSubscriber` role. Microsoft's setup script grants that role to the subscription-writer app as well as to Microsoft.EventGrid, so the webhook also requires the token's v1 `appid` or v2 `azp` to equal `SenderApplicationId`. Read the value from the **Microsoft.EventGrid** service principal in the portal, because it differs between clouds (https://learn.microsoft.com/en-us/azure/event-grid/scripts/powershell-webhook-secure-delivery-microsoft-entra-app). A missing, foreign or conflicting sender claim gets 403.

## Verification

- `tests/CriticalAlerts.Infrastructure.Tests/AzureCommunicationServicesSmsChannelTests.cs`: isolated contract tests F2–F17. The fake transport verifies HMAC independently.
- `tests/CriticalAlerts.Api.IntegrationTests/AcsSmsEndToEndTests.cs`: the API host with PostgreSQL, the real outbox processor, a signature-verifying fake ACS and signed test tokens (F1, F9, F14, F18–F27). It writes `TestResults/phase12/acs-sms-e2e-evidence.json`, which contains only scenario outcomes and counts.

## Provider-neutral voice (slice 2)

Status: implemented and locally verified with the **test-only reference provider**. No real voice provider is registered, so any configured provider name refuses worker and API startup until an approved adapter exists (`REQUIRES_HOSPITAL_DECISION`). Design and failure modes V1–V32: [slice 2 design](../superpowers/specs/2026-10-08-phase-12-slice-2-provider-neutral-voice-design.md).

```text
OutboxDispatchProcessor ── INotificationChannel (Voice)
        │  DescribeSend → ledger: first-send time, callback tag, settings fingerprint (own connection)
        ▼
ProviderVoiceChannel (generic) ── IVoiceCallProvider.CreateCallAsync (test number, generic text, repeats, tag, repeatability ID)
        │  Accepted → Submitted (+ any held callbacks for this tag, in order)
        ▼
POST /api/v1/webhooks/communications/voice/{provider}
        │  IVoiceCallbackReader: authenticate (e.g. JwtCallbackAuthenticator) → parse to closed vocabulary
        ▼
ProviderCallEventService: call ID lookup ─ alert lock ─ dedupe ─ tag ─ state mapping
        └─ unknown call ID, tag matches a committed send → pending_provider_call_events (no alert lock)
```

A concrete provider implements two small ports: `IVoiceCallProvider` (place the call; declare whether creates are idempotent) and `IVoiceCallbackReader` (authenticate, then parse). Everything else is shared:

| Evidence | Attempt state |
| --- | --- |
| Call created (call ID returned) | `Submitted` |
| `answered` | stays `Submitted` (`call-answered` event) |
| `playback-completed` | `Delivered` (not opened, acknowledged or accepted; a voicemail greeting cannot be told apart) |
| `ended` no-answer / busy / declined / unreachable or failed / hung-up or completed before playback | `Failed` `voice-no-answer` / `voice-busy` / `voice-declined` / `voice-call-failed` / `voice-playback-incomplete` |
| `playback-failed` | `Failed` `voice-playback-failed` |
| No terminal event within `CallOutcomeWindowSeconds` (DEMO 180) | `Failed` `call-outcome-unconfirmed`; never called again |
| Ambiguous create, idempotent provider | stays `Requested`; same repeatability ID and first-send time until `UncertainOutcomeWindowSeconds` (DEMO 120), then `provider-outcome-uncertain` |
| Ambiguous create, provider without create deduplication; or any replay of such a provider | `Failed` `provider-outcome-uncertain` immediately; never a second call |
| Replay whose settings fingerprint differs from the ledger | `Failed` `provider-outcome-uncertain` |
| Definite rejection / auth failure | `Failed` `voice-call-rejected` / `provider-auth-failed` |
| Documented non-execution on the first invocation | `Failed` `provider-unavailable`, bounded new attempt |

```text
Communications:Voice:Provider = Simulation | <registered provider name>
Communications:Voice:CallerId = <E.164 test caller ID>
Communications:Voice:CallbackBaseUri = https://<host>
Communications:Voice:TestRecipients:<SIM-VOICE-label> = <approved E.164 test number>
Communications:Voice:Repeats = 2 / RingTimeoutSeconds = 30 / CallOutcomeWindowSeconds = 180 / UncertainOutcomeWindowSeconds = 120   (DEMO)
Communications:Webhooks:Voice:Enabled = false
```

Verification: `tests/CriticalAlerts.Api.IntegrationTests/VoiceEndToEndTests.cs` (API host, PostgreSQL, real outbox processor, reference fake over HTTP, RS256-signed callbacks) writes `TestResults/phase12/voice-e2e-evidence.json` with scenario outcomes and counts only. `src/web/tests/live-failure-vocabulary-contract.test.tsx` keeps the web failure categories equal to the backend projection.

## Not yet verified (human gate)

The master-plan gate still requires three things that have not happened:

- a fake-data **staging** test against a real ACS resource and a real Event Grid subscription;
- proof that no patient detail reaches SMS on a handset;
- completed provider legal and account prerequisites.

None of these have been attempted. `REQUIRES_HOSPITAL_DECISION` items are listed in the design document.
