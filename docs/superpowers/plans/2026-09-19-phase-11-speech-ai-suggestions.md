# Phase 11 implementation ledger

Spec: ../specs/2026-09-19-phase-11-speech-ai-suggestions-design.md. Baseline da7f444799432bc0830088420b2c1fd488579b74. Owner approval recorded before branching. The entire supplied 126-section plan remains the acceptance contract.

## Tasks

1. Documentation and exact baseline (this commit). Files: spec, plan, speech-and-ai-suggestions architecture, AGENTS, README, workflow, definition-of-done, data-model, containers, threat-model, logging-policy, production-readiness-gates.
2. Contracts, feature selection, output/evidence validation, deterministic providers and conservative human application rules. Files: Application/Assistance, Infrastructure/Assistance, application/domain/infrastructure tests. RED missing contracts, then GREEN and relevant regressions.
3. Immutable protected persistence and additive migration. Files: Domain/Assistance, Persistence/Configurations, DbContext, protection purposes, migration; PostgreSQL tests. RED schema/immutability/provenance/protection, then GREEN.
4. Generation/history/Apply orchestration, idempotency and API capabilities/authorization. Files: Infrastructure/Assistance, Api/Http, Program; API/PostgreSQL concurrency tests. RED actual HTTP and relational behaviors, then GREEN.
5. Opt-in Azure Speech transport. Files: Infrastructure/Assistance/AzureSpeechTranscriptionProvider, adapter tests. RED bounded WAV/config/transport/response/cancellation, then GREEN. No live call.
6. Observability safety integration. Files: AuditSafety, PlatformMetrics, runtime sentinel tests and scan scripts. RED observation/assertions, then GREEN.
7. Connected assistance UI. Files: web/lib/assistance, features/connected/assistance-panel, compose-alert, styles, component tests. RED interaction/fallback/stale/evidence/no automatic mutation, then GREEN/typecheck/lint.
8. Evaluation fixtures and harness. Files: tests/fixtures/ai-evaluation, evaluation tests/tool, scripts/run-ai-evaluation.ps1, test-all, CI and ignore files. RED independently computed metrics, then GREEN and safe aggregate results.
9. Connected browser scenarios and full gate. Files: tests/e2e/assistance-system, system harness, OpenAPI artifact, verification package. Run all supplied checks, independent branch review, fix findings with RED/GREEN, record exact counts and limitations. Stop at Phase 11.

## Decisions

Ruling: use one immutable table with closed result kind and separately typed/purpose-protected payloads rather than two structurally duplicate tables — both recommendations in the supplied plan describe the same provenance and immutability boundary; cost if wrong: additive split migration, no loss of evidence.
Ruling: exact extract equality is required in addition to evidence bounds — valid offsets alone do not support arbitrary provider assertions; cost: paraphrases cannot apply in this simulation.
Ruling: conservatively require confirmation of every applied bounded content chunk — covers numeric/unit and other critical content without inventing a clinical detector; cost: extra manual review in simulation.
Ruling: Azure adapter accepts validated WAV PCM only — documented short-audio input, no transcoding; cost: browsers without matching recorder formats retain typing/simulation rather than Azure recording.

## Execution

- Baseline inspection: main 9e312b2, implementation 162a10b, documentation 8117d26, no phase-10 tag/PR/merge. Owner accepted; approval commit da7f444. Clean linked worktree reused.
- Baseline first backend run could not access Docker from sandbox; elevated rerun in progress. No failure counted as a pass.
