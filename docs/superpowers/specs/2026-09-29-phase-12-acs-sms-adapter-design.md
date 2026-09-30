# Phase 12 — Real communication adapter, slice 1: Azure Communication Services SMS

Authorized on 2026-09-29 by the project owner's instruction to start Phase 12 from `main` at `9209b41` (Phase 11 merged through PR #8, post-merge backend 393/393 and web 38/38 passing). Branch: `feature/phase-12-real-communication-adapter`.

The master plan says to deliver Phase 12 one provider at a time. This slice delivers **SMS through Azure Communication Services (ACS) only**. The ACS voice/call-automation adapter is a later Phase 12 slice. It is not started here.

## Scope

In scope:

- An `AzureCommunicationServicesSmsChannel` that implements the existing `INotificationChannel` port behind the transactional outbox.
- **Test-number-only** configuration. The adapter never reads, decrypts or texts a directory contact value.
- Delivery-report intake through a signed Event Grid webhook at `POST /api/v1/webhooks/communications/acs-sms`.
- Normalization of provider results and delivery reports into the existing `Submitted` / `Delivered` / `Failed` attempt dimensions.
- Provider contract tests that use a fake HTTP transport, and one end-to-end backend flow test that writes a repeatable evidence artifact.

Out of scope, all `REQUIRES_HOSPITAL_DECISION` or a later slice:

- Voice, voicemail wording, pager and email.
- Texting real directory numbers.
- Production enablement, sender-number procurement, the ACS contract, residency and subprocessors.
- The Event Grid subscription itself, the Entra app registration and Staging deployment.
- Any change to recipient selection, confirmation, escalation or responsibility.

## Authority

The adapter is a transport. It cannot select recipients, change the approved message, dispatch without a confirmed outbox row, stop escalation or record a response. SMS content is the approved notification policy's generic SMS template, which already requires the `SIMULATION:` prefix. It never contains alert, patient, clinical, recipient or location data. Full case content stays in the authenticated interface.

A provider `202 Accepted` is recorded only as `Submitted`. Only an authenticated ACS delivery report can produce `Delivered`. Delivery never implies opened, acknowledged or responsibility accepted.

## Configuration (fail closed at startup)

`Communications:Sms:Provider` accepts `Simulation` (the default) or `AzureCommunicationServices`. Any other value refuses startup.

When set to `AzureCommunicationServices`:

- The environment must be `Development`, `Test` or `Staging`. `Production` refuses startup. Production enablement is `REQUIRES_HOSPITAL_DECISION`. The dispatch worker itself still runs only in Development/Test until a staging host exists.
- `Endpoint` must be exactly `https://<resource>.communication.azure.com`, with no path, query, user info or port. Redirects are disabled.
- `AccessKey` must be Base64 and at least 32 bytes. It is supplied only through ignored local configuration, user secrets, environment variables or Key Vault, never committed.
- `FromNumber` must be E.164.
- `TestRecipients` maps each synthetic endpoint label (`SIM-SMS-…`) to an approved E.164 test number. It must be non-empty. An endpoint label without a mapping fails the attempt as `test-recipient-not-configured` without an HTTP call.
- `DeliveryReportWindowSeconds` (DEMO default 300, range 60–43200) bounds how long `Submitted` may wait for a delivery report.
- `UncertainOutcomeWindowSeconds` (DEMO default 120, range 10–3600) bounds same-key resubmission after an ambiguous outcome.

Both windows are technical DEMO values. They are not clinical or production service levels.

The API pinned is ACS SMS `2021-03-07` (GA): `POST /sms?api-version=2021-03-07` with HMAC-SHA256 access-key signing (`x-ms-date`, `x-ms-content-sha256`, `host`). References checked 2026-09-29:

- https://learn.microsoft.com/en-us/rest/api/communication/sms/sms/send
- https://learn.microsoft.com/en-us/rest/api/communication/authentication

## Idempotency and duplicate prevention

Each delivery attempt already has a stable idempotency key. The adapter derives:

- `repeatabilityRequestId`: a deterministic UUID from SHA-256 of the provider name and attempt key.
- `repeatabilityFirstSent`: the attempt's durable `RequestedAtUtc` in RFC 1123 format.

Re-invoking the same attempt therefore sends identical repeatability values, and ACS executes it at most once.

Ambiguous outcomes leave the attempt `Requested` and are retried with the **same** attempt key until the uncertain window closes. They are then failed visibly as `provider-outcome-uncertain`. Ambiguous outcomes are a timeout, a transport error after the send began, an overall 5xx or 408, or an unreadable response.

Only definite non-acceptance may use the existing bounded `provider-unavailable` retry, which creates a new attempt number: an overall 429, or an item-level 429/5xx without a message ID.

The `tag` sent to ACS is `ca-` plus 32 hex characters of the SHA-256 of the attempt key. The webhook recomputes it and rejects a report whose tag does not match the attempt that owns the message ID.

## Webhook authentication and validation

Event Grid delivers with a Microsoft Entra bearer token. The dedicated JWT bearer scheme `EventGridWebhook` validates:

- the signature against tenant signing keys;
- the issuer (the tenant's v1 or v2 issuer);
- the audience (the configured webhook app ID or URI);
- the lifetime, with 2 minutes of skew;
- the role `AzureEventGridSecureWebhookSubscriber`.

Development cookies and every other scheme are not accepted on this route. The endpoint exists only when `Communications:Webhooks:EventGrid:Enabled` is true, `TenantId`, `Audience` and `ExpectedTopic` are configured, and the environment is not Production.

Request limits:

- Only `application/json` is accepted.
- The body is at most 64 KiB.
- A batch holds at most 50 events.
- A fixed-window, address-partitioned rate limit applies.

Each event must have:

- a safe `id` of at most 100 characters;
- a `topic` equal to `ExpectedTopic`, compared case-insensitively;
- the supported `eventType`;
- `dataVersion` `1.0`;
- an `eventTime` no older than 48 hours and no more than 5 minutes in the future.

The data must have a safe `messageId` and a `deliveryStatus` of `Delivered` or `Failed`. The `from`, `to`, `subject` and `deliveryStatusDetails` values are never read into the model, persisted, logged or audited. A `Microsoft.EventGrid.SubscriptionValidationEvent` is answered with its validation code only after authentication. Any other event type rejects the batch.

Each report is processed in its own transaction:

1. Find the attempt by provider and message ID.
2. Take the alert mutation lock.
3. Check the inbox record `(organization, event id, handler)` and the unique delivery event, which makes duplicates no-ops.
4. Verify the tag.
5. Apply the terminal status through the existing no-regression domain methods.
6. Append a delivery event with fixed sanitized metadata, a `dispatch.delivery-event` audit record (actor `provider-webhook`) and an inbox record.

Unknown message IDs return success with no stored data, because a report could belong to another deployment. Tag mismatches are recorded as rejected in the inbox, with no state change.

## Failure modes (written before the code)

Every row has a planned test. "Isolated" means a fake-transport contract test. "E2E" means the API host, a real PostgreSQL container and the real outbox processor.

| # | Failure | Required behavior | Test |
| --- | --- | --- | --- |
| F1 | Provider left at default | Simulation channel used, no network | E2E |
| F2 | ACS configured in Production | Startup refused | isolated |
| F3 | Endpoint not an ACS https host / has path, query, port | Startup refused | isolated |
| F4 | Access key missing or not Base64 ≥32 bytes | Startup refused, key never echoed | isolated |
| F5 | TestRecipients empty or value not E.164 | Startup refused | isolated |
| F6 | Endpoint label not in TestRecipients | Attempt `Failed` `test-recipient-not-configured`, zero HTTP calls | isolated |
| F7 | Wake-up text lacks `SIMULATION:`, is over 160 chars or is non-printable | `DispatchValidationException`, zero HTTP calls | isolated |
| F8 | Request must be correctly signed | Signature recomputed by the fake with the test key matches; body is only from/to/message/options | isolated |
| F9 | 202 with successful item | `Submitted` only, provider reference is the message ID, never `Delivered` | isolated + E2E |
| F10 | Same attempt re-invoked | Identical repeatability ID and first-sent | isolated |
| F11 | Item 400 (for example an invalid number) | `Failed` `sms-rejected`, not retried | isolated |
| F12 | Overall 401/403 | `Failed` `provider-auth-failed`, not retried | isolated |
| F13 | Overall 429 or item 5xx/429 without message ID | `Failed` `provider-unavailable`, bounded retry | isolated |
| F14 | Timeout, transport error, overall 5xx/408, malformed JSON | Attempt stays `Requested`, retry uses same key; after window `provider-outcome-uncertain` | isolated + E2E |
| F15 | repeatabilityResult `rejected` | `Failed` `provider-repeatability-rejected` | isolated |
| F16 | Message ID unsafe or over 100 chars | Treated as uncertain; never persisted | isolated |
| F17 | Redirect response | Not followed, treated as uncertain | isolated |
| F18 | No delivery report within window | `Failed` `delivery-unconfirmed`, visible, manual fallback | E2E |
| F19 | Webhook without token / wrong audience / wrong issuer / expired / missing role / bad signature / dev cookie | 401 or 403, nothing stored | E2E |
| F20 | Webhook wrong content type, oversize, over 50 events, unsupported event type, wrong topic, stale or future event | 4xx, nothing stored | E2E |
| F21 | Subscription validation | Echoes code only when authenticated | E2E |
| F22 | Delivered report | Attempt `Delivered`, outbox completes, live projection shows delivered but not acknowledged/accepted | E2E |
| F23 | Duplicate report (same event id) | No second event, no state change, 200 | E2E |
| F24 | Out-of-order: Failed after Delivered | No regression | E2E |
| F25 | Unknown message ID | 200, nothing stored | E2E |
| F26 | Tag mismatch | Rejected in inbox, no state change | E2E |
| F27 | Phone numbers, access key, token, message body in DB/logs/audit/problems | Absent (sentinel scan) | E2E |

## Evidence artifact

The E2E test writes `TestResults/phase12/acs-sms-e2e-evidence.json`. The file contains:

- scenario names and outcomes;
- attempt statuses and failure categories;
- event, inbox and audit counts;
- the fake transport's request count and signature-verification result.

It never contains numbers, keys, tokens or message bodies. The test is deterministic and repeatable: fixed fictional data, a fake transport and a controllable clock.

## Open decisions (REQUIRES_HOSPITAL_DECISION)

- Sender number and registration (toll-free verification or 10DLC).
- The approved SMS wording.
- Real-recipient enablement.
- Delivery-report and uncertain windows.
- Opt-out and STOP handling.
- Data residency.
- The ACS contract and subprocessors.
- Retention of provider message IDs.
- Event Grid subscription ownership and the Entra app registration.
- Staging deployment.
- Incident response for provider outages.
