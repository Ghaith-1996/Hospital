# Phase 11 — Speech and AI suggestions

Authorized on 2026-09-19 by the supplied Phase 11 specification and explicit owner approval of Phase 10. Exact approved baseline: da7f444799432bc0830088420b2c1fd488579b74 (acceptance record), following 8117d26f38419f352cc824441cb076ce3e325288 (verification documentation) and 162a10b74c6a961f127d1682c6ecd0a12918dfb7 (verified implementation). Branch: feature/phase-11-speech-ai-suggestions. No Phase 12.

## Authority and representations

Human source, immutable provider transcription, operator-applied source revision, immutable structured suggestion, editable operator SBAR, approved final message and exact-version critical confirmations are separate. The legacy Alert.StructuredSuggestion property remains the editable SBAR storage; it is not the new immutable provider result. Provider generation cannot call alert mutations, select recipients, assign urgency, approve messages, confirm fields, dispatch, stop escalation or accept responsibility. Apply is a separate authenticated AlertDraftEditor command.

Both feature flags default false. Backend capabilities select Disabled, Simulated, or opt-in AzureSpeech for transcription and Disabled/Simulated for structuring. Credentials never enable a feature. Providers run only in Development/Test. Misconfiguration yields unavailable capabilities and safe failures while typing remains usable. No real LLM adapter, real communication provider, callback, production identity or hospital connector.

## Contracts and validation

Application ports receive bounded audio plus an allowlisted language/scenario, or exact source and language only. No directory, urgency, recipient or responsibility dependencies. Results use nullable confidence; absence is not certainty. Validate lengths, language/provider/version vocabularies, segment count/times, field paths, confidence ranges, missing/ambiguity counts and evidence bounds. Evidence uses zero-based UTF-16 [start,endExclusive); valid support must also exactly reproduce the proposed extract. Offsets alone cannot bless fabricated content. Unsupported and ambiguous fields are excluded from Apply; missing fields stay missing. The deterministic structurer extracts explicit English/French SBAR labels and preserves negation, decimals, units and contradictions as text. It never invents facts or recommendations.

## Persistence and concurrency

Use one additive immutable assistance-results table with a closed Transcription/Structuring kind, organization/alert/version/source-revision/actor provenance, provider/configuration version, UTC creation and dedicated purpose-protected payloads. The payload types and protection purposes stay separate. Organization/alert/source/version composite foreign keys prevent mismatched provenance. Database and EF mutation guards preserve history. No audio column, file, blob or request buffering to disk.

Generation validates exact editable version, claims existing organization/operation/key idempotency in a short transaction, commits a safe requested audit, calls provider outside a database transaction, then atomically persists immutable output, completion audit and opaque result reference. A concurrent source edit does not discard provenance: the result is saved for its original version and becomes stale. Same exact request replays; changed request conflicts; a started operation is never automatically reinvoked after an uncertain crash. Explicit new generation with a new key is a human decision. Failures complete the operation with a fixed category, without raw response or exception text. No retries inside adapters.

Apply takes the existing alert mutation lock, rechecks exact version/source/kind/organization/editability and revalidates output. UpdateSource or SetStructuredSuggestion creates the normal new draft version, invalidates approval and carries manually selected recipients and approved-message bytes unchanged. A bounded simulation lexer registers exact numeric tokens (including decimal/date/time forms) and explicit allowlisted units as unresolved fields. A separate fixed-text human review attestation covers all source information, including laterality and medication-like terms. Full transcripts never enter plaintext confirmation columns. This is not a clinical terminology detector. Missing/unsupported fields preserve existing manual SBAR values; absent values remain absent and require manual completion. Normal manual APIs remain primary.

## Audio and Azure

Raw body only, maximum existing 2 MiB, bounded read even without Content-Length; reject empty, oversized or unsupported input before provider invocation. Clear managed buffers when finished. Audio digest belongs only in the one-way request identity, never public metadata. UI records only an intersection of backend accepted types and MediaRecorder support, with size/time bounds and track cleanup. No intersection or microphone denial keeps typing available.

Azure uses short-audio REST with server-only credentials, fixed trusted Azure endpoint construction, cancellation, bounded response, no redirects and safe errors. Restrict adapter to verified WAV PCM 16 kHz mono 16-bit input, at most 60 seconds, validated before egress. No assumed WebM support or transcoder. Reference checked 2026-09-19: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-speech-to-text-short . Adapter contract tests use fake HTTP transport; live smoke requires separate explicit request and fictional audio.

## UI, observability and evaluation

Compose shows separate transcript and source/suggestion panels, evidence slices, nullable confidence, missing/ambiguous/unsupported fields and explicit consequence-labelled Apply controls. Save/discard unsaved edits before assistance; preserve edits on errors; block double submission and retain uncertain operation identity. Stale results stay visible with Apply disabled. Authorized history reads use no-store and bounded cursor pagination. No browser persistence or analytics.

Use Phase 10 committed audit/log/metric boundary for requested/completed/failed/applied/stale events. Only fixed actions and bounded counts; never audio, source, transcript, suggestion, evidence, credential, exception or provider payload. Extend runtime sentinels and safe vocabulary.

Network-free evaluation fixtures cover English, French, code switching, noise metadata, numbers/units, similar/decimal/missing numbers, negation, medication-like text, omissions, abbreviation ambiguity and contradictory statements. Report expected-token exact match, unit exact match, omitted expected facts / expected facts, unsupported fields / nonempty fields, fictional operator-corrected fields / presented fields, and all metrics by language. Zero denominator reports null. Operator corrections are fictional fixture expectations. Results are engineering checks, not clinical accuracy or production thresholds.

## Open decisions and gate

REQUIRES_HOSPITAL_DECISION: real-data speech permission; provider; residency; retention; contract/subprocessors; raw-audio retention permission/duration; real LLM provider/residency/training/retention; production thresholds; approved terminology/critical fields/languages; monitoring; human training; AI incident response; production role mapping. No hospital decision is inferred.

Complete the supplied backend/PostgreSQL/UI/concurrency/security/evaluation/OpenAPI/restore/browser/container/fresh-migration gate and document exact executed outcomes. Stop for owner review; do not publish, merge or tag automatically.

## Local configuration and transport

Only Development or Test is accepted. Defaults in API appsettings are disabled. To exercise deterministic suggestions, explicitly set `Features__SpeechTranscription=true`, `Features__AlertStructuringSuggestions=true`, `Speech__Provider=Simulated`, and `AlertStructuring__Provider=Simulated` in the local API process. Setting a provider or credential alone enables nothing. The worker does not invoke these providers. Container defaults remain off; pass optional API environment values explicitly, never bake them into images or frontend variables.

For optional fictional Azure tests, set `Speech__Provider=AzureSpeech`, `Speech__AzureResourceName` and `Speech__AzureKey` only in the local backend secret environment, with the speech flag explicitly true. The resource hostname is validated and the adapter fixes HTTPS, path and no redirects. No arbitrary provider URL is accepted. Azure short-audio accepts only validated RIFF WAV, PCM 16 kHz, mono, 16-bit, at most 60 seconds. The browser records only an accepted MediaRecorder type; browsers without WAV support retain manual typing. No transcoder is included. Confidence and detected language are null when Azure does not supply them; a language hint is never presented as detection.

API routes below require the existing AlertDraftEditor authorization and organization scope:

| Route below `/api/v1` | Purpose |
| --- | --- |
| `GET /capabilities` | Safe enabled flags, speech provider and accepted media types |
| `POST /alerts/{id}/transcriptions` | Bounded raw audio; `X-Alert-Draft-Version`, `Idempotency-Key`, optional `X-Audio-Language-Hint` |
| `POST /alerts/{id}/structuring-suggestions` | JSON `expectedVersion`, idempotency key |
| `GET /alerts/{id}/transcriptions` or `/structuring-suggestions` | 20-item cursor history with original source and immutable result |
| `POST /alerts/{id}/transcriptions/{result}/apply` or `/structuring-suggestions/{result}/apply` | Separate JSON `expectedVersion` and idempotency key |

Simulation scenarios use `X-Simulation-Scenario` only for the simulated Development/Test provider. Audio is at most 2 MiB. Supported hints: en-CA, en-US, fr-CA, fr-FR. Provider source/transcript is bounded to 16,000 UTF-16 code units; four SBAR fields, at most 4,000 per field; 100 segments; eight evidence spans per field; Azure response at most 128 KiB. Provider deadlines are 20 seconds for Azure and a cooperative 25-second orchestration deadline. There is no provider retry. API errors use fixed codes, protected responses use no-store, and the existing request limit/rate limiter applies.

Human Apply invalidates prior approval using the normal version increment. Content, recipients and message remain independent. Results generated before a concurrent edit remain queryable with their original provenance and stale status. A repeated exact completed generation or Apply key replays the same result/version. A shared key with changed content conflicts. An interrupted Started claim is deliberately not reinvoked; retry checks status, and an explicitly new user action may use a new key. Lost Apply refresh locks assistance until the saved draft reloads. Browser reload never generates or applies a suggestion.

Raw audio uses in-memory bounded buffers only, cleared when disposed at backend boundaries. Browser buffers are transient, recorder duration is 55 seconds, and tracks stop on completion, error or unmount. Managed strings cannot promise immediate physical erasure. No file/blob/audio column, analytics payload, local/session storage, IndexedDB, service worker, or Cache API is used for assistance. `Permissions-Policy` limits microphone to the same origin and disables camera. React text rendering escapes provider text; no HTML interpretation or new remote script origin is added.

## Evaluation formulas and limitations

Run `pwsh scripts/run-ai-evaluation.ps1`. CI always uses deterministic simulated providers with byte fixtures containing no speech. The 14 authored fictional cases include English, French, code switching, unclear numbers/noise metadata, decimals, missing units, negation, ambiguous abbreviations and contradictory labels. Each contains expected numeric tokens, number-unit pairs, expected factual fields, missing/ambiguous paths and fictional operator-approved field values. The report contains only aggregate counts/rates in ignored `artifacts/ai-evaluation/summary.json`.

- Number exact match: multiplicity-aware exact expected numeric tokens found / expected numeric tokens. No decimal normalization. This is recall, not a claim that no extra number was emitted.
- Unit pair exact match: exact expected number-and-unit pairs found / expected pairs. No invented-unit credit or fuzzy matching.
- Omission: expected nonambiguous factual fields absent from available suggestion / expected factual fields. Expected paths are fixture-authored, including intentionally empty expected sets for ambiguous content.
- Unsupported inference: generated factual fields lacking exact matching source evidence / all generated factual fields. This must be zero for the deterministic extractor; ambiguous source text still has evidence and stays blocked from Apply.
- Human correction: fields differing from the fixture's fictional operator-approved values / generated fields. It measures scripted corrections, not actual users.
- Supplemental word error: whitespace-token Levenshtein edits / reference words, only for transcription fixtures. The simulator does not recognize audio; this is harness validation, not speech-recognition accuracy.

All zero denominators return null. Report each metric separately for English, French and CodeSwitch. No production threshold or multilingual clinical performance claim follows from this small deterministic fixture set. Other languages, approved terminology, retention/residency, provider agreements and production quality thresholds remain REQUIRES_HOSPITAL_DECISION.

For an explicitly requested local Azure sample only, run `pwsh scripts/run-azure-speech-evaluation.ps1 -LocalApiUrl http://127.0.0.1:5080 -FictionalWavePath <local.wav> -FictionalReferencePath <local.txt> -ConfirmFictionalAudio`. It refuses CI/non-loopback, requires the explicitly configured Azure backend, reads a bounded local fictional WAV and SIMULATION-marked reference, and emits only numeric aggregates. It creates an unconfirmed fictional draft and protected transcription history, never applies or dispatches. Raw audio is not retained by this app. Provider-side handling requires a separate approval before real data; this command does not establish one. Live Azure execution is optional and is not part of the normal gate.
