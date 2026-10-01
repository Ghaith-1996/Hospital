# Real communication adapters (Phase 12)

Status: slice 1, Azure Communication Services (ACS) SMS, is implemented and locally verified with fake transports only. No live ACS request has been made. Voice, pager and email are not implemented. The design and failure modes are in [the Phase 12 slice 1 design](../superpowers/specs/2026-09-29-phase-12-acs-sms-adapter-design.md).

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
| Overall 429, item 429/5xx without message ID | `Failed` `provider-unavailable` | Existing bounded retry with a new attempt |
| Overall/item 4xx | `Failed` `sms-rejected` | Not retried |
| Overall 401/403 | `Failed` `provider-auth-failed` | Not retried; configuration incident |
| Repeatability `rejected` | `Failed` `provider-repeatability-rejected` | Not retried |
| Endpoint label without a test mapping | `Failed` `test-recipient-not-configured` | No network call |

An unmatched report less than 10 minutes old gets `503` with `Retry-After`, and nothing is stored. It may have raced the worker transaction that stores the message ID, so Event Grid redelivers it. An unmatched report older than that is accepted and dropped. A `202` body of the wrong JSON shape is treated as an ambiguous outcome and retried with the same key.

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
Communications:Webhooks:EventGrid:TenantId / Audience / ExpectedTopic (ACS resource) / ValidationTopic (Event Grid topic of the subscription)
```

The worker validates the SMS settings at startup, even when dispatch is disabled. The API validates the webhook settings at startup. Production refuses both.

Event Grid setup:

- The subscription must use the **Event Grid schema**. The CloudEvents OPTIONS handshake is not implemented.
- The subscription must filter to `Microsoft.Communication.SMSDeliveryReportReceived`.
- Microsoft Entra authentication must be enabled with the webhook app registration, which grants the `AzureEventGridSecureWebhookSubscriber` role.

## Verification

- `tests/CriticalAlerts.Infrastructure.Tests/AzureCommunicationServicesSmsChannelTests.cs`: isolated contract tests F2–F17. The fake transport verifies HMAC independently.
- `tests/CriticalAlerts.Api.IntegrationTests/AcsSmsEndToEndTests.cs`: the API host with PostgreSQL, the real outbox processor, a signature-verifying fake ACS and signed test tokens (F1, F9, F14, F18–F27). It writes `TestResults/phase12/acs-sms-e2e-evidence.json`, which contains only scenario outcomes and counts.

## Not yet verified (human gate)

The master-plan gate still requires three things that have not happened:

- a fake-data **staging** test against a real ACS resource and a real Event Grid subscription;
- proof that no patient detail reaches SMS on a handset;
- completed provider legal and account prerequisites.

None of these have been attempted. `REQUIRES_HOSPITAL_DECISION` items are listed in the design document.
