# Phase 11 verification and owner review

Status: final gate and independent review in progress. This document does not record owner acceptance.

## Exact baseline and scope

Owner-approved Phase 10 documentation: `8117d26f38419f352cc824441cb076ce3e325288`, following verified implementation `162a10b74c6a961f127d1682c6ecd0a12918dfb7`. The owner's explicit approval was recorded before Phase 11 as `da7f444799432bc0830088420b2c1fd488579b74`, the exact branch baseline. Implementation branch: `feature/phase-11-speech-ai-suggestions`, using the existing linked worktree. The main checkout remained at `9e312b2`; no push, PR, merge, tag, visibility change or Phase 12 work is included.

Whole-branch review input: `fc975d4179c28cbbc82731b265ac21a7125c2b76`. Final verified implementation head and final gate results will be recorded after completion.

## Delivered behavior

Both features default off. Backend capabilities and enforcement restrict all generation/Apply to Development/Test, explicit flags and supported provider configuration. The typed workflow, human critical confirmation, exact final review, simulation dispatch, delivery, responsibility and escalation remain primary. Provider contracts have no authority over urgency, recipients, approved message, dispatch or escalation.

Deterministic transcription returns named fictional scenarios, including English/French/code switching, ambiguous numbers, decimals, missing units, negation, contradictions and outage. It does not recognize audio. The structurer extracts explicit source SBAR labels; it does not generate recommendations or interpret clinical meaning. Nullable confidence, missing/ambiguous paths and exact source evidence remain visible. Evidence must reproduce proposed text exactly; unsupported and ambiguous fields cannot apply. Existing manual values survive missing/unsupported fields.

`alert_assistance_results` is an immutable protected history table with separate result kinds/payload types/purposes. It preserves organization, alert, exact version/source, actor, provider/configuration and UTC provenance. Composite FKs prevent mismatches; PostgreSQL denies UPDATE/DELETE/TRUNCATE and EF rejects entity mutation. New assistance encryption authenticates purpose and organization (`local-v2-context`); historical purposes retain their existing format. No raw audio storage is added.

Generation claims idempotency durably, invokes a provider outside a database transaction, then atomically stores protected output and completion evidence. Same-key duplicates invoke once; changed request identity conflicts. Concurrent edits preserve the original result as stale history. Apply locks and checks the exact editable draft, creates a normal new version and leaves critical information unresolved. Number/unit tokens use existing confirmation fields; a fixed-text full-source attestation covers laterality, medication-like text and content outside the deliberately limited numeric lexer. Full transcript chunks are not placed in plaintext confirmation fields.

Compose shows separate transcript and source/SBAR review panels, confidence, evidence, missing information and history. Request and Apply are explicit human actions. Recording is bounded and transient; tracks stop on completion/error/unmount. Denied microphones, incompatible formats and provider outages leave typing usable. Double submissions are blocked; uncertain requests retain the same key; refresh does not regenerate. Tests exercise keyboard actions, a 390-pixel layout and absence of local/session/IndexedDB/Cache storage. No clinical-content screenshots are captured.

The optional Azure adapter uses fixed HTTPS resource endpoint construction, no redirects/retries, validated PCM WAV, bounded response/deadline and fixed errors. It cannot enable itself from credentials. Fake HTTP transport tests cover failures/malformed output/cancellation. The optional local-only aggregate evaluation command refuses CI. **No live Azure request was made.**

Audit actions and metrics use the existing committed observation boundary, fixed vocabulary and finite operation labels. Runtime sentinels cover success, Apply, stale failure and provider exception paths; protected content and audio do not enter logs, audit metadata, metrics, health or problems. No remote exporter is added.

## Verification

The full `scripts/test-all.ps1` gate is running. It includes locked restore, format, Release build, real PostgreSQL tests, dependency checks, frontend tests/typecheck/lint/production build, OpenAPI verification, sensitive-data/storage/observability scans, evaluation, standalone and connected browsers, real restore/failure cleanup and all three image builds/proxy validation. Final counts and image IDs are pending.

Focused executed results before the final gate:

| Check | Result |
| --- | --- |
| Backend regression before final test additions | 653 passed |
| Assistance HTTP boundaries/concurrency/post-confirmation | 20 passed |
| Result-domain protection/UTC invariants | 3 passed |
| Azure fake transport | 12 passed |
| Phase 10 finalized-schema upgrade/provenance FK | 1 passed |
| Evaluation metrics/harness | 4 passed |
| Web component regression | 67 passed |
| Web typecheck/lint/production build | Passed |
| Connected assistance browser scenarios | 2 passed, complete teardown |
| Optional Azure script syntax/CI rejection | Passed; no network/audio read |

The migration test constructs the finalized Phase 10 schema by migrating and downgrading the empty isolated database, inserts a fictional historical source, then upgrades. It verifies original readability/version and rejects a result tied to the wrong source version. Fresh schema/seed/readiness are additionally exercised by the isolated system harness. Migration `20260919193851_Phase11AssistanceResults` is additive; historical migrations are unchanged.

## Safe evaluation report

Dataset `DEMO-1`: 14 fictional cases, 13 successful outputs and one expected simulated outage. Outputs below describe deterministic fixture behavior, not real speech or clinical accuracy. Formula definitions and commands are in [speech and AI architecture](../architecture/speech-and-ai-suggestions.md).

| Group | Cases | Exact expected numbers | Exact number/unit pairs | Omitted expected fields | Unsupported fields | Fictional correction fields |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| English | 10 | 6/6 | 2/3 | 0/8 | 0/12 | 2/12 |
| French | 2 | 4/4 | 2/2 | 0/6 | 0/6 | 0/6 |
| CodeSwitch | 2 | 2/2 | 2/2 | 0/4 | 0/4 | 0/4 |
| Total | 14 | 12/12 | 6/7 | 0/18 | 0/22 | 2/22 |

There are 30 missing-field markers and four ambiguity markers. Supplemental simulated transcription word error is 3/75. Numeric matching is exact expected-token recall, not proof of absence of extra values. The ambiguous 18-or-80 fixture deliberately has an incorrect number/unit pairing requiring manual review. Zero denominators are null. Reports contain no transcript/source/evidence text and are ignored by git and Docker.

## Review findings and deviations

Independent whole-branch review is pending. The [implementation ledger](plans/2026-09-19-phase-11-speech-ai-suggestions.md) records RED/GREEN outcomes and rulings:

- One closed-kind immutable table replaces two structurally duplicate suggested tables; separate payload types/purposes preserve representation boundaries.
- Exact extract equality is required beyond valid offsets; paraphrases are not automatically applicable.
- Full transcript confirmation chunks were rejected after a failing plaintext sentinel regression. Exact numeric/unit fields and a fixed full-source human attestation avoid that leak without claiming clinical extraction.
- Azure supports validated short PCM WAV only; browsers without matching formats keep typing. No automatic transcoding or live-provider test is claimed.
- New purpose/organization binding is limited to new assistance purposes to preserve historical protected data.

## Remaining human decisions

Phase 11 owner review/acceptance remains pending. Real-data permission, provider contracts, residency, subprocessors, retention/training terms, raw-audio policy, approved clinical terminology/languages, thresholds, operator training, AI incident governance, production identity and integration decisions remain `REQUIRES_HOSPITAL_DECISION`. No production suitability or autonomous clinical behavior is claimed. Stop at Phase 11.

Proposed consolidated change description: `feat: add simulation-only speech and evidence-backed suggestions with explicit human application`.
