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

Apply takes the existing alert mutation lock, rechecks exact version/source/kind/organization/editability and revalidates output. UpdateSource or SetStructuredSuggestion creates the normal new draft version, invalidates approval and carries manually selected recipients and approved-message bytes unchanged. Applied content is conservatively registered for human confirmation in bounded text chunks (including numbers, units, dates, laterality and medication-like terms); this avoids claiming a clinical classifier. Missing SBAR fields remain null and block submission until manually completed. Normal manual APIs remain primary.

## Audio and Azure

Raw body only, maximum existing 2 MiB, bounded read even without Content-Length; reject empty, oversized or unsupported input before provider invocation. Clear managed buffers when finished. Audio digest belongs only in the one-way request identity, never public metadata. UI records only an intersection of backend accepted types and MediaRecorder support, with size/time bounds and track cleanup. No intersection or microphone denial keeps typing available.

Azure uses short-audio REST with server-only credentials, fixed trusted Azure endpoint construction, cancellation, bounded response, no redirects and safe errors. Restrict adapter to verified WAV PCM 16 kHz mono 16-bit input, at most 60 seconds, validated before egress. No assumed WebM support or transcoder. Reference checked 2026-09-19: https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-speech-to-text-short . Adapter contract tests use fake HTTP transport; live smoke requires separate explicit request and fictional audio.

## UI, observability and evaluation

Compose shows separate transcript and source/suggestion panels, evidence slices, nullable confidence, missing/ambiguous/unsupported fields and explicit consequence-labelled Apply controls. Save/discard unsaved edits before assistance; preserve edits on errors; block double submission and retain uncertain operation identity. Stale results stay visible with Apply disabled. Authorized history reads use no-store and bounded cursor pagination. No browser persistence or analytics.

Use Phase 10 committed audit/log/metric boundary for requested/completed/failed/applied/stale events. Only fixed actions and bounded counts; never audio, source, transcript, suggestion, evidence, credential, exception or provider payload. Extend runtime sentinels and safe vocabulary.

Network-free evaluation fixtures cover English, French, code switching, noise metadata, numbers/units, similar/decimal/missing numbers, negation, medication-like text, omissions, abbreviation ambiguity and contradictory statements. Report expected-token exact match, unit exact match, omitted expected facts / expected facts, unsupported fields / nonempty fields, corrected fields / presented fields, and all metrics by language. Zero denominator reports null. Operator corrections are fictional fixture expectations. Results are engineering checks, not clinical accuracy or production thresholds.

## Open decisions and gate

REQUIRES_HOSPITAL_DECISION: real-data speech permission; provider; residency; retention; contract/subprocessors; raw-audio retention permission/duration; real LLM provider/residency/training/retention; production thresholds; approved terminology/critical fields/languages; monitoring; human training; AI incident response; production role mapping. No hospital decision is inferred.

Complete the supplied backend/PostgreSQL/UI/concurrency/security/evaluation/OpenAPI/restore/browser/container/fresh-migration gate and document exact executed outcomes. Stop for owner review; do not publish, merge or tag automatically.
