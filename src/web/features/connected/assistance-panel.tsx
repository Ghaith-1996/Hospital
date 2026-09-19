"use client";
import React from 'react';
import * as api from '../../lib/assistance';
import { createIdempotencyKey, getAlertDraft, isAlertApiError, type AlertDraft } from '../../lib/alerts';
const MAX_AUDIO = 2 * 1024 * 1024;
const confidence = (value: number | null) => value === null ? 'Confidence not provided' : `${value < 0.6 ? 'Low confidence · ' : ''}Provider confidence: ${value.toFixed(2)}`;
export function AssistancePanel({ draft, disabled, onApplied, onBusy }: {
    draft: AlertDraft;
    disabled: boolean;
    onApplied(value: AlertDraft): void;
    onBusy(value: boolean): void;
}) {
    const [capabilities, setCapabilities] = React.useState<api.Capabilities | null>(null);
    const [results, setResults] = React.useState<api.AssistanceResult[]>([]);
    const [selected, setSelected] = React.useState<string | null>(null);
    const [cursors, setCursors] = React.useState<Partial<Record<keyof typeof api.kinds, string | null>>>({});
    const [busy, setBusy] = React.useState(false);
    const [recording, setRecording] = React.useState(false);
    const [status, setStatus] = React.useState('');
    const [error, setError] = React.useState('');
    const [uncertain, setUncertain] = React.useState(false);
    const [refreshRequired, setRefreshRequired] = React.useState(false);
    const [historyRevision, setHistoryRevision] = React.useState(0);
    const [scenario, setScenario] = React.useState('clear-en');
    const lock = React.useRef(false);
    const alive = React.useRef(true);
    const recorder = React.useRef<MediaRecorder | null>(null);
    const stream = React.useRef<MediaStream | null>(null);
    const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null);
    const pending = React.useRef<{
        key: string;
        run(key: string): Promise<api.AssistanceResult | void>;
        applies: boolean;
    } | null>(null);
    const heading = React.useRef<HTMLHeadingElement>(null);
    React.useEffect(() => { alive.current = true; return () => { alive.current = false; if (timer.current)
        clearTimeout(timer.current); if (recorder.current) {
        recorder.current.onstop = null;
        recorder.current.ondataavailable = null;
        if (recorder.current.state !== 'inactive')
            recorder.current.stop();
    } stream.current?.getTracks().forEach(track => track.stop()); pending.current = null; }; }, []);
    React.useEffect(() => {
        let cancelled = false;
        void api.getCapabilities().then(async (caps) => {
            if (cancelled)
                return;
            setCapabilities(caps);
            const history = await Promise.all(([...(caps.speechTranscription ? ['Transcription' as const] : []), ...(caps.alertStructuringSuggestions ? ['Structuring' as const] : [])]).map(async (kind) => ({ kind, page: await api.getHistory(draft.alertId, kind) })));
            if (cancelled)
                return;
            setResults(current => {
                const loaded = history.flatMap(item => item.page.items);
                return [...loaded, ...current.filter(item => item.alertId === draft.alertId && !loaded.some(row => row.id === item.id))];
            });
            setCursors(Object.fromEntries(history.map(item => [item.kind, item.page.nextCursor])));
        }).catch(() => { if (!cancelled)
            setStatus('Optional assistance history is unavailable. Continue with manual editing.'); });
        return () => { cancelled = true; };
    }, [draft.alertId, draft.draftVersion, historyRevision]);
    const setWorking = (value: boolean) => { if (alive.current) {
        setBusy(value);
        onBusy(value);
    } };
    async function execute(run: (key: string) => Promise<api.AssistanceResult | void>, applies = false, retry = false) {
        if (lock.current || disabled || refreshRequired || (!retry && pending.current))
            return;
        pending.current ??= { key: createIdempotencyKey(), run, applies };
        lock.current = true;
        setWorking(true);
        setError('');
        setStatus(applies ? 'Applying reviewed suggestion…' : 'Requesting suggestion…');
        const attempt = pending.current;
        try {
            const result = await attempt.run(attempt.key);
            pending.current = null;
            if (!alive.current)
                return;
            setUncertain(false);
            if (attempt.applies) {
                setRefreshRequired(true);
                const current = await getAlertDraft(draft.alertId);
                if (!alive.current)
                    return;
                onApplied(current);
                setResults(items => items.map(item => ({ ...item, stale: true })));
                setRefreshRequired(false);
                setStatus('Applied. Review and reconfirm critical information.');
            }
            else if (result) {
                setResults(items => [result, ...items.filter(item => item.id !== result.id)]);
                setSelected(result.id);
                setStatus('Suggestion ready for review. Nothing has been applied.');
            }
            setTimeout(() => heading.current?.focus(), 0);
        }
        catch (failure) {
            if (!alive.current)
                return;
            if (!pending.current) {
                setError('The action was saved, but the draft could not be refreshed. Reload the saved draft before another action.');
                setRefreshRequired(true);
            }
            else if (isAlertApiError(failure) && failure.code === 'operation-in-progress') {
                setUncertain(true);
                setError('This request is still in progress. Retry the same request to check its result, or continue with manual editing.');
            }
            else if (isAlertApiError(failure) && ((failure.status >= 400 && failure.status < 500 && ![408, 429].includes(failure.status))
                || (failure.status === 503 && ['provider-unavailable', 'provider-output-invalid'].includes(failure.code ?? '')))) {
                pending.current = null;
                setUncertain(false);
                if (isAlertApiError(failure) && failure.status === 409) {
                    setResults(items => items.map(item => ({ ...item, stale: true })));
                    setError('The draft changed or this suggestion is from an older source version. Reload the draft and explicitly request a new suggestion.');
                }
                else
                    setError('Assistance is temporarily unavailable. You can continue with manual editing.');
            }
            else {
                setUncertain(true);
                setError('Assistance status is uncertain. Retry the same request, or continue with manual editing.');
            }
            setStatus('');
        }
        finally {
            lock.current = false;
            setWorking(false);
        }
    }
    async function refreshDraft() { if (lock.current)
        return; lock.current = true; setWorking(true); try {
        onApplied(await getAlertDraft(draft.alertId));
        setRefreshRequired(false);
        setError('');
    }
    catch {
        setError('Saved draft is unavailable. Continue after refreshing.');
    }
    finally {
        lock.current = false;
        setWorking(false);
    } }
    async function more(kind: keyof typeof api.kinds) { const cursor = cursors[kind]; if (!cursor || lock.current)
        return; lock.current = true; setWorking(true); try {
        const page = await api.getHistory(draft.alertId, kind, cursor);
        setResults(items => [...items, ...page.items.filter(item => !items.some(old => old.id === item.id))]);
        setCursors(value => ({ ...value, [kind]: page.nextCursor }));
    }
    catch {
        setError('History is unavailable. Continue with manual editing.');
    }
    finally {
        lock.current = false;
        setWorking(false);
    } }
    const format = capabilities?.acceptedAudioContentTypes.find(type => typeof MediaRecorder !== 'undefined' && MediaRecorder.isTypeSupported(type));
    async function record() {
        if (lock.current || disabled || !format || pending.current)
            return;
        lock.current = true;
        setWorking(true);
        setError('');
        setStatus('Requesting microphone…');
        try {
            const media = await navigator.mediaDevices.getUserMedia({ audio: true });
            if (!alive.current) {
                media.getTracks().forEach(track => track.stop());
                return;
            }
            stream.current = media;
            const capture = new MediaRecorder(media, { mimeType: format });
            recorder.current = capture;
            let chunks: Blob[] = [];
            let size = 0;
            let oversized = false;
            capture.ondataavailable = event => { size += event.data.size; if (size > MAX_AUDIO) {
                oversized = true;
                chunks = [];
                if (capture.state !== 'inactive')
                    capture.stop();
            }
            else if (!oversized)
                chunks.push(event.data); };
            capture.onstop = () => {
                if (timer.current)
                    clearTimeout(timer.current);
                media.getTracks().forEach(track => track.stop());
                stream.current = null;
                recorder.current = null;
                lock.current = false;
                setWorking(false);
                setRecording(false);
                if (!alive.current)
                    return;
                if (oversized) {
                    setError('Recording exceeded the size limit. Continue by typing or record a shorter fictional sample.');
                    return;
                }
                const audio = new Blob(chunks, { type: format });
                chunks = [];
                void execute(key => api.transcribe(draft.alertId, draft.draftVersion, key, audio));
            };
            capture.onerror = () => { oversized = true; chunks = []; if (capture.state !== 'inactive')
                capture.stop(); };
            capture.start(250);
            setRecording(true);
            setStatus('Recording fictional dictation. Stop when finished.');
            timer.current = setTimeout(() => { if (capture.state !== 'inactive')
                capture.stop(); }, 55000);
        }
        catch {
            stream.current?.getTracks().forEach(track => track.stop());
            stream.current = null;
            lock.current = false;
            setWorking(false);
            setError('Microphone access is unavailable. Continue by typing the alert.');
            setStatus('');
        }
    }
    if (!capabilities || !capabilities.speechTranscription && !capabilities.alertStructuringSuggestions)
        return null;
    const result = results.find(item => item.id === selected) ?? results[0];
    const stale = !!result && (result.stale || result.alertVersion !== draft.draftVersion);
    const unavailable = disabled || busy || uncertain || refreshRequired;
    return <section className="detail-card assistance-panel" aria-label="Optional speech and structuring assistance">
  <h2 ref={heading} tabIndex={-1}>Optional assistance · simulation only</h2>
  <p>Suggestions require human review. Urgency, recipients, the approved message and dispatch remain under your control.</p>
  {disabled && <p>Save or discard your unsaved edits before requesting or applying assistance.</p>}
  <p role="status" aria-live="polite">{status}</p>{error && <p role="alert">{error}</p>}
  {uncertain && <><button type="button" disabled={busy || disabled} onClick={() => { const attempt = pending.current; if (attempt)
        void execute(attempt.run, attempt.applies, true); }}>Retry same assistance request</button><button type="button" disabled={busy} onClick={() => { pending.current = null; setUncertain(false); setHistoryRevision(value => value + 1); setError('Previous request may still complete. Review saved history before requesting again.'); }}>Stop retrying and reload history</button></>}
  {refreshRequired && <button type="button" disabled={busy || disabled} onClick={() => void refreshDraft()}>Reload saved draft</button>}
  <div className="form-actions">
   {capabilities.alertStructuringSuggestions && <button type="button" disabled={unavailable} onClick={() => void execute(key => api.generateStructure(draft.alertId, draft.draftVersion, key))}>Suggest SBAR structure</button>}
   {capabilities.speechTranscription && (recording ? <button type="button" onClick={() => recorder.current?.stop()}>Stop recording</button> : <button type="button" disabled={unavailable || !format} onClick={() => void record()}>Record dictation</button>)}
  </div>
  {capabilities.speechTranscription && !format && <p>This browser and provider have no compatible recording format. Continue by typing.</p>}
  {capabilities.speechProvider === 'Simulated' && <div className="assistance-sample"><label>Fictional dictation scenario <select value={scenario} disabled={unavailable} onChange={event => setScenario(event.target.value)}>{['clear-en', 'clear-fr', 'code-switch', 'low-number', 'missing-unit', 'abbreviation', 'decimal', 'negation', 'contradiction', 'provider-outage'].map(value => <option key={value}>{value}</option>)}</select></label><button type="button" disabled={unavailable} onClick={() => void execute(key => api.transcribe(draft.alertId, draft.draftVersion, key, new Blob([new Uint8Array([1, 2, 3])], { type: 'audio/wav' }), scenario))}>Use fictional dictation sample</button><p>The simulator returns a fixed fictional scenario; it does not recognize recorded speech.</p></div>}
  {results.length > 0 && <label>Saved suggestion history <select value={result?.id ?? ''} disabled={busy} onChange={event => setSelected(event.target.value)}>{results.map(item => <option key={item.id} value={item.id}>{item.kind} · version {item.alertVersion} · {new Date(item.createdAtUtc).toLocaleString()}</option>)}</select></label>}
  {Object.entries(cursors).filter(([, cursor]) => !!cursor).map(([kind]) => <button type="button" key={kind} disabled={busy} onClick={() => void more(kind as keyof typeof api.kinds)}>Older {kind.toLowerCase()} results</button>)}
  {result && <div className="assistance-review">
   <p>{result.provider} · {result.providerVersion} · draft version {result.alertVersion}</p>
   {stale && <p>This suggestion was generated from an older source version. Generate a new suggestion before applying it.</p>}
   {result.transcription && <section><h3>Transcription suggestion</h3><p>{result.transcription.detectedLanguage ?? 'Language not detected'}</p><p>{confidence(result.transcription.confidence)}</p><pre>{result.transcription.transcript}</pre>{result.transcription.segments.map((segment, index) => <p key={index}>{segment.startMilliseconds}–{segment.endMilliseconds} ms · {segment.text} · {confidence(segment.confidence)}</p>)}<p>This will replace the current editable source with this transcript and create a new draft version. You must review and reconfirm critical information.</p><button type="button" disabled={unavailable || stale} onClick={() => void execute(key => api.applyResult(draft.alertId, result, draft.draftVersion, key), true)}>Use transcript as source</button></section>}
   {result.suggestion && <><div className="assistance-columns"><section><h3>Source used for this suggestion</h3><pre>{result.sourceText}</pre></section><section><h3>Structured suggestion</h3><p>{confidence(result.suggestion.confidence)}</p>{result.suggestion.fields.map(field => <section className="assistance-field" key={field.path}><h4>{field.path}</h4><p>{field.value}</p><p>{confidence(field.confidence)}</p>{api.supported(result.sourceText, field) ? field.evidence.map((span, index) => <p key={index}>Evidence [{span.start}, {span.endExclusive}): <mark>{result.sourceText.slice(span.start, span.endExclusive)}</mark></p>) : <p>{field.ambiguous ? 'Ambiguous information — requires manual review.' : 'Unsupported suggestion — no matching source evidence.'}</p>}</section>)}</section></div><p>Missing information: {result.suggestion.missingFields.join(', ') || 'None reported'}</p><p>Ambiguous information: {result.suggestion.ambiguities.join(', ') || 'None reported'}</p><p>Only evidence-backed fields will be copied. Critical numbers and units still require human confirmation. Missing and ambiguous fields remain unresolved; existing manual values are preserved.</p><button type="button" disabled={unavailable || stale || !result.suggestion.fields.some(field => api.supported(result.sourceText, field))} onClick={() => void execute(key => api.applyResult(draft.alertId, result, draft.draftVersion, key), true)}>Apply evidence-backed suggestion</button></>}
  </div>}
 </section>;
}
