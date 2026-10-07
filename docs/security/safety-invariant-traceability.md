# Safety Invariant Traceability

## Purpose

This table maps each repository safety invariant (see [AGENTS.md](../../AGENTS.md)) to the executable test or design control that evidences it. It was promoted from the Phase 9 verification ledger and extended with Phase 10 and Phase 11 evidence before those historical documents were consolidated into [development history](../archive/development-history.md). Test files live under `tests/` (backend projects, `tests/e2e`) and `src/web/tests`. Phase 0 controls remain design requirements for integrations that are deliberately absent.

Passing tests are simulation evidence only. They do not approve production use; every unresolved hospital decision remains `REQUIRES_HOSPITAL_DECISION` (see [production readiness gates](production-readiness-gates.md)).

Coverage note: commits `9209b41` and `561b6b6` (2026-09-29 and 2026-09-30) removed duplicate and redundant tests. Every file named below was re-verified to exist after those commits. Where a previously cited test file no longer exists, the row says so and names the current covering test.

## Invariant to evidence

| Required invariant | Executable evidence or explicit design control |
|---|---|
| Fictional data only; no PHI, contacts or secrets in artifacts | `CriticalAlerts.Architecture.Tests/RepositorySafetyTests.cs` (no tracked env file, no non-synthetic phone pattern, no provider credential pattern); `scripts/verify-no-sensitive-data.ps1`; synthetic system fixtures. Boundary: [data classification](data-classification.md), [logging policy](logging-policy.md). |
| No autonomous AI clinical decisions, routing, dispatch or stop | Phase 0 [threat model](threat-model.md) AI boundary. Escalation can activate only the exact human-approved stored recipient plan: `Infrastructure.Tests/EscalationProcessorTests.cs` (exact activation, missing-reference failure). Phase 11 suggestions are immutable evidence applied only by a human: `Api.IntegrationTests/AssistanceBoundaryTests.cs`, `AssistanceSafetyTests.cs`, `AssistanceApiTests.cs`, `Domain.Tests/AssistanceResultTests.cs`, `Infrastructure.Tests/AssistanceStorageTests.cs`; see [speech and AI suggestions](../architecture/speech-and-ai-suggestions.md). |
| Authorized human confirms exact content, version, values, units, recipients, channels and policy | `Api.IntegrationTests/AlertReviewTests.cs` and `AlertConfirmationTests.cs` (missing or mismatched plan, changed directory evidence, exact snapshot, transactional rollback, replay). |
| Any content or recipient edit invalidates approval | `Domain.Tests/AlertStateMachineTests.cs` (edit returns to Draft and increments version; recipient and message edits carry forward to a new version); API draft concurrency, review and confirmation suites. Escalation activates only recipients approved at the unchanged version. |
| Original source, structured content and approval stay separate | `Domain.Tests/AlertSourceRevisionTests.cs`, `Infrastructure.Tests/AlertSourceRevisionPersistenceTests.cs`, `Infrastructure.Tests/LegacySensitiveDataMigrationTests.cs`; design control in [data classification](data-classification.md) for transcription and suggestions. |
| Every critical number and unit requires human confirmation | `Domain.Tests/AlertStateMachineTests.cs` (unresolved critical field blocks submission and confirmation; confirmation requires the recorded value and unit; one canonical confirmation per version and field); API review and confirmation tests. |
| Generic SMS and voicemail; details only in the authenticated UI | `Infrastructure.Tests/SimulationChannelTests.cs` (generic wake-up text through the typed channel ports) and `Domain.Tests/AlertStateMachineTests.cs` `OutboxRejectsClinicalPayloads`. Coverage removed in `9209b41` — REVIEW: the former `Application.Tests/DispatchContractTests.cs` (opaque references, no clinical payload in dispatch request, operational-only delivery status view) no longer exists; identifier-only outbox payload is still asserted by `Api.IntegrationTests/AlertConfirmationTests.cs` and `Infrastructure.Tests/OutboxDispatchProcessorTests.cs`. DEMO escalation uses SecureMessage only. Production wording: [ADR 0004](../adr/0004-no-phi-in-wakeup-channels.md) and readiness gates. |
| Delivered, opened, acknowledged and responsibility accepted stay separate | `Domain.Tests/RecipientResponseStateTests.cs`, `Domain.Tests/EscalationRunTests.cs`, `Infrastructure.Tests/OutboxDispatchProcessorTests.cs`, `Infrastructure.Tests/ResponseLifecycleConcurrencyTests.cs`; system scenarios A and D-I in `tests/e2e/closed-loop-system.spec.ts` (acknowledgement still escalates; accepted responsibility stops it). |
| Unsupported channel states are `NotApplicable` | `Domain.Tests/RecipientResponseStateTests.cs` `NonSecureMessageOpenRemainsNotApplicable`; `Api.IntegrationTests/AlertLiveAuthorizationTests.cs`; `src/web/tests/connected-responses.test.tsx` (renders `Opened: NotApplicable`). |
| External callbacks authenticated, validated, replay-resistant and idempotent | No external endpoint exists. Phase 0 threat-model provider boundary and production readiness gates remain required. Simulation duplicate and out-of-order provider events are covered by `Domain.Tests/DispatchWorkerStateTests.cs` and `Infrastructure.Tests/OutboxDispatchProcessorTests.cs`. |
| Delivery and provider failures stay visible | `Infrastructure.Tests/OutboxDispatchProcessorTests.cs` (including backup outage preserving the original recipient response path); safe failure and fallback projection in `Api.IntegrationTests/AlertLiveAuthorizationTests.cs` and `Infrastructure.Tests/OperationalWarningProjectionTests.cs`; frontend `src/web/tests/connected-responses.test.tsx`. |
| Audit is organization-scoped, role-restricted, append-only and non-disclosing (Phase 10) | `Api.IntegrationTests/AuditQueryTests.cs` (role, scope, cursor, metadata projection), `Infrastructure.Tests/AuditStorageTests.cs` (direct UPDATE/DELETE rejected, original evidence survives), `Application.Tests/AuditSafetyTests.cs`; see [observability](../architecture/observability.md). |
| Logs, metrics and health never carry protected values (Phase 10) | `Api.IntegrationTests/ObservabilitySafetyTests.cs`, `Infrastructure.Tests/PlatformMetricsTests.cs`, `Infrastructure.Tests/OperationalLoggingTests.cs`, `scripts/verify-observability-safety.ps1`. |
| No persistent browser workflow state | `scripts/verify-web-storage-safety.ps1` (active routes, features and API clients); design control in [containers](../architecture/containers.md#connected-browser-boundary). |
| No secrets committed | `scripts/verify-no-sensitive-data.ps1`, `RepositorySafetyTests.cs`, ignored local runtime secrets; generated system-harness credentials are ephemeral and untracked. |

## Escalation and response behavior (Phase 9)

| Behavior | Evidence |
|---|---|
| Exact-version, organization-scoped runs; legacy alerts stay disabled | `Infrastructure.Tests/EscalationPersistenceTests.cs`, `EscalationProcessorTests.cs` |
| Evaluation precedence, pause and resume remaining delay, consumed negative responses | `Domain.Tests/EscalationRunTests.cs`, `Infrastructure.Tests/EscalationOverrideTests.cs` |
| Lock ordering with response and lifecycle commands | `Infrastructure.Tests/ResponseLifecycleConcurrencyTests.cs`, `EscalationProcessorTests.cs` |
| Authorization, idempotency and non-disclosure of escalation controls | `Api.IntegrationTests/EscalationApiTests.cs` |
| Restart, deadline and two-worker behavior against real PostgreSQL, worker and browser | `tests/e2e/closed-loop-system.spec.ts` scenarios D to I |

## Related documents

- [Threat model](threat-model.md), [data classification](data-classification.md), [logging policy](logging-policy.md), [production readiness gates](production-readiness-gates.md)
- [Alert state machine](../architecture/alert-state-machine.md), [simulated dispatch](../architecture/simulated-dispatch.md), [observability](../architecture/observability.md)
- [Development history](../archive/development-history.md)
