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
Ruling: exact numeric tokens/allowlisted units remain unresolved, plus a fixed full-source review attestation — full transcript chunks would leak into the legacy plaintext confirmation columns (demonstrated by a failing sentinel test). Cost: limited simulation lexer is not a clinical detector; laterality/medication terminology needs full human review and future hospital-approved rules.
Ruling: Azure adapter accepts validated WAV PCM only — documented short-audio input, no transcoding; cost: browsers without matching recorder formats retain typing/simulation rather than Azure recording.

## Execution

- Baseline inspection: main 9e312b2, implementation 162a10b, documentation 8117d26, no phase-10 tag/PR/merge. Owner accepted; approval commit da7f444. Clean linked worktree reused.
- Baseline first backend run could not access Docker from sandbox; elevated rerun in progress. No failure counted as a pass.
- Task 2: contract RED missing Assistance namespace; GREEN application 84/84 (15 new), simulated provider 11/11. Baseline elevated run passed 589/589. Provider infrastructure regression recorded in local phase11-provider-regression.log. Documentation whitespace check corrected.
- Task 2 infrastructure regression: 218/218 passed.
- Task 3 RED missing result entity, then a real failure: wrong-organization decrypt succeeded in the pre-existing protection adapter. Added authenticated organization/purpose context for new assistance purposes only; legacy data retains its original format. GREEN storage 2/2 and infrastructure regression 220/220. Additive migration 20260919193851_Phase11AssistanceResults adds exact source composite FK and immutable statement trigger.
- Ruling: new assistance purposes use local-v2-context authenticated encryption while existing purposes retain local-v1 — fulfills cross-organization cryptographic binding without rewriting Phase 10 data; cost: future key/protection migration must support both formats.
- Task 5: adapter RED missing transport then invalid .NET media-header parsing. Fixed bounded WAV transport with raw documented Content-Type header. GREEN 12 fake-HTTP cases cover absent confidence/language, HTTP failures, malformed/oversized output, invalid audio and cancellation/no retry. Configuration uses AzureResourceName to match the documented resource endpoint; no live call. Domain/upgrade/evaluation focus also passed 20 tests.
- Tasks 4/6: API and concurrency RED missing routes/services; runtime observation RED unknown audit actions. GREEN 20 focused HTTP tests, 26 metric tests; prior whole API regression 231/231. Provider work occurs outside transactions; duplicate generation calls once; concurrent edits preserve stale provenance; Apply mutates once. Protected-storage sentinel RED found full transcript chunks in legacy confirmation fields; replaced with exact numeric/unit extraction and fixed full-source attestation, GREEN. Post-confirmation and cross-org guards, unsupported/malformed evidence, body limits and cursor paging pass. Phase 10 upgrade fixture uses actual finalized historical schema (latest migration followed by downgrade to Phase 10), then upgrades with preserved source; wrong source/version FK is rejected. Domain provenance 3/3 passed.
