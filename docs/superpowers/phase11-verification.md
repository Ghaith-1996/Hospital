# Phase 11 verification and owner review

Status: The pre-rebase implementation and local verification completed on 2026-09-19. This PR has since been rebased onto the current Phase 9 main; the recorded checks below describe the original reviewed source and do not verify the rebased tree. Post-rebase verification and project-owner Phase 11 acceptance remain pending.

## Exact baseline and scope

Owner-approved Phase 10 documentation: `8117d26f38419f352cc824441cb076ce3e325288`, following verified implementation `162a10b74c6a961f127d1682c6ecd0a12918dfb7`. The owner's explicit approval was recorded before Phase 11 as `da7f444799432bc0830088420b2c1fd488579b74`, the exact branch baseline. Implementation branch: `feature/phase-11-speech-ai-suggestions`, using the existing linked worktree. The main checkout remained at `9e312b2`; no push, PR, merge, tag, visibility change or Phase 12 work is included.

Whole-branch review input: `fc975d4179c28cbbc82731b265ac21a7125c2b76`. The four review fixes were committed as **`9e3af0a6f6fbff46c8995073a4e4f617d1fb2902`**, the exact implementation head used for the final complete gate. Subsequent closure changes are documentation only.

## Delivered behavior

Both features default off. Backend capabilities and enforcement restrict all generation/Apply to Development/Test, explicit flags and supported provider configuration. The typed workflow, human critical confirmation, exact final review, simulation dispatch, delivery, responsibility and escalation remain primary. Provider contracts have no authority over urgency, recipients, approved message, dispatch or escalation.

Deterministic transcription returns named fictional scenarios, including English/French/code switching, ambiguous numbers, decimals, missing units, negation, contradictions and outage. It does not recognize audio. The structurer extracts explicit source SBAR labels; it does not generate recommendations or interpret clinical meaning. Nullable confidence, missing/ambiguous paths and exact source evidence remain visible. Evidence must reproduce proposed text exactly; unsupported and ambiguous fields cannot apply. Existing manual values survive missing/unsupported fields.

`alert_assistance_results` is an immutable protected history table with separate result kinds/payload types/purposes. It preserves organization, alert, exact version/source, actor, provider/configuration and UTC provenance. Composite FKs prevent mismatches; PostgreSQL denies UPDATE/DELETE/TRUNCATE and EF rejects entity mutation. New assistance encryption authenticates purpose and organization (`local-v2-context`); historical purposes retain their existing format. No raw audio storage is added.

Generation claims idempotency durably, invokes a provider outside a database transaction, then atomically stores protected output and completion evidence. Same-key duplicates invoke once; changed request identity conflicts. Concurrent edits preserve the original result as stale history. Apply locks and checks the exact editable draft, creates a normal new version and leaves critical information unresolved. Number/unit tokens use existing confirmation fields; a fixed-text full-source attestation covers laterality, medication-like text and content outside the deliberately limited numeric lexer. Full transcript chunks are not placed in plaintext confirmation fields.

Compose shows separate transcript and source/SBAR review panels, confidence, evidence, missing information and history. Request and Apply are explicit human actions. Recording is bounded and transient; tracks stop on completion/error/unmount. Denied microphones, incompatible formats and provider outages leave typing usable. Double submissions are blocked; uncertain requests retain the same key; refresh does not regenerate. Tests exercise keyboard actions, a 390-pixel layout and absence of local/session/IndexedDB/Cache storage. No clinical-content screenshots are captured.

The optional Azure adapter uses fixed HTTPS resource endpoint construction, no redirects/retries, validated PCM WAV, bounded response/deadline and fixed errors. It cannot enable itself from credentials. Fake HTTP transport tests cover failures/malformed output/cancellation. The optional local-only aggregate evaluation command refuses CI. **No live Azure request was made.**

Audit actions and metrics use the existing committed observation boundary, fixed vocabulary and finite operation labels. Runtime sentinels cover success, Apply, stale failure and provider exception paths; protected content and audio do not enter logs, audit metadata, metrics, health or problems. No remote exporter is added.

## Verification

The full `scripts/test-all.ps1` gate completed with **exit 0** on the exact implementation head above. Every command in that script executed; none was substituted with an in-memory database or live speech provider.

| Check | Result |
| --- | --- |
| Locked .NET restore, format, Release build | Passed; 0 warnings / 0 errors |
| Complete backend suite | **673 passed**: Domain 95, Application 84, Infrastructure 246, API 239, Architecture 9; 0 skipped/failures |
| Included Phase 11 coverage | Assistance HTTP 27, result domain 3, Azure fake transport 12, upgrade/provenance 1, evaluation 6; contract/provider/storage tests also included |
| .NET vulnerable-package scan | No vulnerable packages reported in all 11 projects |
| Root/web npm clean install and production audit | Passed; 0 vulnerabilities reported |
| Web component regression | **69 passed** in 11 files |
| Web typecheck/lint/production build | Passed |
| OpenAPI complete runtime comparison | Passed, deterministic 3.1 contract |
| Sensitive-data, browser-storage and observability checks | Passed; focused runtime observability 51 API + 30 infrastructure tests |
| Evaluation metrics/harness | 6 passed, 14 authored fictional cases |
| Standalone production browser smoke | 1 passed |
| Connected full workflow with assistance disabled | 14 passed, including typed dispatch/response/responsibility/escalation and new flags-off case |
| Connected assistance enabled | 2 passed; transcript/structure Apply, stale/outage, keyboard/mobile, no automatic dispatch/storage |
| Real PostgreSQL restore and injected cleanup | Passed, both AfterBackup and AfterRestore cleanup verified |
| API, worker and web images; separate web-container proxy | All passed |
| Fresh migrations, fictional seed and API readiness | Passed in isolated connected harness and restore source |
| Optional Azure script syntax/CI rejection | Passed; no network/audio read |
| Full aggregate test-all | **Passed, exit 0** |

The migration test constructs the finalized Phase 10 schema by migrating and downgrading the empty isolated database, inserts a fictional historical source, then upgrades. It verifies original readability/version and rejects a result tied to the wrong source version. Fresh schema/seed/readiness are additionally exercised by the isolated system harness. Migration `20260919193851_Phase11AssistanceResults` is additive; historical migrations are unchanged.

Both connected harnesses reported `container_remaining=0`, `live_owned_processes=0`, `live_worker_processes=0`, `ports_closed=1`. The full workflow started 33 owned workers during restart scenarios; the assistance run started one. Two-worker evidence reports `dispatch=true escalation=true cardinality=1`. Post-run Docker inspection found no remaining system/restore containers.

Safe restore evidence: schema and relational invariants matched; safe counts matched; restored database readable; source unchanged; append-only audit verified. Temporary database, dump and owned container were removed. Measured restore exercise: backup 274 ms, restore 575 ms, validation 2144 ms, total 11192 ms. Both injected failure stages reached their checkpoint and verified cleanup. This proves local fictional schema/restore mechanics, not production volume, key recovery or RPO/RTO.

Final local image IDs:

```text
critical-alerts-api:verification    sha256:707fd867f332bf5c5709793acbba1a2142157211025f13090fa1833c85ffbdc4
critical-alerts-worker:verification sha256:1b2baa30905df73dfe7d52dedf42370e4f373d8958b5f35b905a6ebb4a9a1600
critical-alerts-web:verification    sha256:1d2d17accf23d41c1d2391b5d8324fc8f1aab6e13f3db50a4bf874ca50dbece0
```

Executed final gate command: `pwsh scripts/test-all.ps1`, with the local .NET 10.0.100 / Node 24.16.0 tools and existing Chromium cache on PATH. The script executes locked restore, `dotnet format --verify-no-changes`, Release build/tests, vulnerability scan, root/web `npm ci`, production audit, frontend tests/typecheck/lint/build, OpenAPI/safety scripts, `run-ai-evaluation.ps1`, standalone Playwright, both system harness modes, restore/cleanup scripts, three `docker build` commands and `verify-web-container.ps1`. Detailed runtime logs, TRX and aggregate evaluation output remain local/untracked; only safe evidence is recorded here.

## Safe evaluation report

Dataset `DEMO-1`: 14 fictional cases, 13 successful outputs and one expected simulated outage. Outputs below describe deterministic fixture behavior, not real speech or clinical accuracy. Formula definitions and commands are in [speech and AI architecture](../architecture/speech-and-ai-suggestions.md).

| Group | Cases | Exact expected numbers | Exact number/unit pairs | Omitted expected facts | Unsupported fields | Fictional correction fields |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| English | 10 | 6/6 | 2/3 | 0/8 | 0/12 | 2/12 |
| French | 2 | 4/4 | 2/2 | 0/6 | 0/6 | 0/6 |
| CodeSwitch | 2 | 2/2 | 2/2 | 0/4 | 0/4 | 0/4 |
| Total | 14 | 12/12 | 6/7 | 0/18 | 0/22 | 2/22 |

There are 30 missing-field markers and four ambiguity markers. Supplemental simulated transcription word error is 3/75. Numeric matching is exact expected-token recall, not proof of absence of extra values. The ambiguous 18-or-80 fixture deliberately has an incorrect number/unit pairing requiring manual review. Zero denominators are null. Reports contain no transcript/source/evidence text and are ignored by git and Docker.

## Review findings and deviations

An independent read-only whole-branch review found no Critical findings, three Important findings and one initially Minor finding. The history race was re-graded Important because it hides a successful result from the operator. All four were fixed in one pass, with failing regressions observed before changes and a complete green final gate afterward:

1. Leading decimals/signs/exponents and `%` preserve exact human confirmation values. Apply-level regression: six failures/one pass before fix, seven passes after. Evaluation token scoring also rejects false equivalence between `.5` and `5`.
2. Generic/unknown 503 responses retain the pending key for potentially committed operations; same-key retry succeeds. Only explicit terminal provider codes retire it. HTTP 408 is also treated as uncertain.
3. Omission checks authored fact text in the correct field, not section presence. A present section missing a critical fact fails the new negative scoring test before the fix and passes afterward.
4. Late history responses merge with newer results. The deferred-history test proves they cannot erase a newly generated suggestion.

The reviewer did not run the full suite, accept live Azure behavior or judge production clinical thresholds; those limits are retained. No second review is claimed. The [implementation ledger](plans/2026-09-19-phase-11-speech-ai-suggestions.md) records RED/GREEN outcomes and rulings:

- One closed-kind immutable table replaces two structurally duplicate suggested tables; separate payload types/purposes preserve representation boundaries.
- Exact extract equality is required beyond valid offsets; paraphrases are not automatically applicable.
- Full transcript confirmation chunks were rejected after a failing plaintext sentinel regression. Exact numeric/unit fields and a fixed full-source human attestation avoid that leak without claiming clinical extraction.
- Azure supports validated short PCM WAV only; browsers without matching formats keep typing. No automatic transcoding or live-provider test is claimed.
- New purpose/organization binding is limited to new assistance purposes to preserve historical protected data.

Physical microphone capture and live Azure recognition were not exercised; browser recording lifecycle uses controlled MediaRecorder tests, and connected flows use fictional byte samples. The 0.6 low-confidence label is a simulation display cue, not calibration or a clinical acceptance threshold. There is no real LLM, automatic specialty recommendation, analytics exporter, production provider or hospital integration.

## Remaining human decisions

Phase 11 owner review/acceptance remains pending. Real-data permission, provider contracts, residency, subprocessors, retention/training terms, raw-audio policy, approved clinical terminology/languages, thresholds, operator training, AI incident governance, production identity and integration decisions remain `REQUIRES_HOSPITAL_DECISION`. No production suitability or autonomous clinical behavior is claimed. Stop at Phase 11.

Proposed consolidated change description: `feat: add simulation-only speech and evidence-backed suggestions with explicit human application`.

## Files changed

The exact changed-file inventory from the approved Phase 10 baseline through the verified implementation is below. The closure commit updates only documentation, AGENTS and README.

```text
.dockerignore
.github/workflows/ci.yml
.gitignore
AGENTS.md
README.md
docs/api/openapi.json
docs/architecture/containers.md
docs/architecture/data-model.md
docs/architecture/speech-and-ai-suggestions.md
docs/product/definition-of-done.md
docs/product/workflow.md
docs/security/logging-policy.md
docs/security/production-readiness-gates.md
docs/security/threat-model.md
docs/superpowers/phase11-verification.md
docs/superpowers/plans/2026-09-19-phase-11-speech-ai-suggestions.md
docs/superpowers/specs/2026-09-19-phase-11-speech-ai-suggestions-design.md
playwright.system.config.ts
scripts/run-ai-evaluation.ps1
scripts/run-azure-speech-evaluation.ps1
scripts/system-e2e.ps1
scripts/test-all.ps1
scripts/verify-observability-safety.ps1
scripts/verify-web-storage-safety.ps1
src/backend/CriticalAlerts.Api/Http/AssistanceEndpoints.cs
src/backend/CriticalAlerts.Api/Program.cs
src/backend/CriticalAlerts.Api/appsettings.json
src/backend/CriticalAlerts.Application/Assistance/AssistanceContracts.cs
src/backend/CriticalAlerts.Application/Assistance/AssistanceWorkflow.cs
src/backend/CriticalAlerts.Application/Audit/AuditSafety.cs
src/backend/CriticalAlerts.Domain/Assistance/AssistanceResult.cs
src/backend/CriticalAlerts.Infrastructure/Assistance/AssistanceRegistration.cs
src/backend/CriticalAlerts.Infrastructure/Assistance/AssistanceService.cs
src/backend/CriticalAlerts.Infrastructure/Assistance/AzureSpeechTranscriptionProvider.cs
src/backend/CriticalAlerts.Infrastructure/Assistance/SimulatedProviders.cs
src/backend/CriticalAlerts.Infrastructure/Observability/PlatformMetrics.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/AssistanceResultConfiguration.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260919193851_Phase11AssistanceResults.Designer.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/20260919193851_Phase11AssistanceResults.cs
src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/CriticalAlertsDbContextModelSnapshot.cs
src/backend/CriticalAlerts.Infrastructure/Protection/AesGcmSensitiveDataProtector.cs
src/web/app/globals.css
src/web/features/connected/assistance-panel.tsx
src/web/features/connected/compose-alert.tsx
src/web/lib/assistance.ts
src/web/next.config.ts
src/web/tests/connected-assistance.test.tsx
tests/CriticalAlerts.Api.IntegrationTests/AssistanceApiTests.cs
tests/CriticalAlerts.Api.IntegrationTests/AssistanceBoundaryTests.cs
tests/CriticalAlerts.Api.IntegrationTests/AssistanceSafetyTests.cs
tests/CriticalAlerts.Api.IntegrationTests/DevelopmentAuthenticationTests.cs
tests/CriticalAlerts.Application.Tests/AssistanceContractTests.cs
tests/CriticalAlerts.Domain.Tests/AssistanceResultTests.cs
tests/CriticalAlerts.Infrastructure.Tests/AssistanceEvaluationHarnessTests.cs
tests/CriticalAlerts.Infrastructure.Tests/AssistanceEvaluationTests.cs
tests/CriticalAlerts.Infrastructure.Tests/AssistanceStorageTests.cs
tests/CriticalAlerts.Infrastructure.Tests/AzureSpeechAdapterTests.cs
tests/CriticalAlerts.Infrastructure.Tests/EvaluationMetrics.cs
tests/CriticalAlerts.Infrastructure.Tests/Phase11MigrationTests.cs
tests/CriticalAlerts.Infrastructure.Tests/PlatformMetricsTests.cs
tests/CriticalAlerts.Infrastructure.Tests/SimulatedAssistanceTests.cs
tests/e2e/assistance-system.spec.ts
tests/fixtures/ai-evaluation/cases.json
```
