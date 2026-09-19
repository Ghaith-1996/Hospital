import { requestJson } from './alerts';
export type Capabilities = {
    speechTranscription: boolean;
    alertStructuringSuggestions: boolean;
    speechProvider: string;
    acceptedAudioContentTypes: string[];
    simulationOnly: true;
};
export type SuggestedField = {
    path: string;
    value: string;
    evidence: {
        start: number;
        endExclusive: number;
    }[];
    confidence: number | null;
    ambiguous: boolean;
};
export type Suggestion = {
    fields: SuggestedField[];
    missingFields: string[];
    ambiguities: string[];
    confidence: number | null;
    provider: string;
    providerVersion: string;
    configurationVersion: string;
};
export type Transcription = {
    transcript: string;
    segments: {
        startMilliseconds: number;
        endMilliseconds: number;
        text: string;
        confidence: number | null;
    }[];
    detectedLanguage: string | null;
    confidence: number | null;
    provider: string;
    providerVersion: string;
};
export type AssistanceResult = {
    id: string;
    alertId: string;
    alertVersion: number;
    sourceRevisionId: string;
    createdAtUtc: string;
    kind: 'Transcription' | 'Structuring';
    provider: string;
    providerVersion: string;
    configurationVersion: string;
    stale: boolean;
    sourceText: string;
    transcription: Transcription | null;
    suggestion: Suggestion | null;
};
export type AssistancePage = {
    items: AssistanceResult[];
    nextCursor: string | null;
};
export const kinds = { Transcription: 'transcriptions', Structuring: 'structuring-suggestions' } as const;
export async function getCapabilities(): Promise<Capabilities> {
    const value = await requestJson<Capabilities>('/api/v1/capabilities');
    if (typeof value?.speechTranscription !== 'boolean' || typeof value.alertStructuringSuggestions !== 'boolean' || !Array.isArray(value.acceptedAudioContentTypes) || value.simulationOnly !== true)
        throw new Error('Capabilities unavailable');
    return value;
}
export async function getHistory(alert: string, kind: keyof typeof kinds, cursor?: string): Promise<AssistancePage> {
    const page = await requestJson<AssistancePage>(`/api/v1/alerts/${encodeURIComponent(alert)}/${kinds[kind]}${cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''}`);
    if (!Array.isArray(page?.items) || page.items.length > 20)
        throw new Error('History unavailable');
    return page;
}
export function generateStructure(alert: string, version: number, key: string): Promise<AssistanceResult> {
    return requestJson(`/api/v1/alerts/${encodeURIComponent(alert)}/structuring-suggestions`, { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify({ expectedVersion: version }) });
}
export function transcribe(alert: string, version: number, key: string, audio: Blob, scenario?: string): Promise<AssistanceResult> {
    return requestJson(`/api/v1/alerts/${encodeURIComponent(alert)}/transcriptions`, { method: 'POST', headers: { 'Idempotency-Key': key, 'X-Alert-Draft-Version': String(version), 'Content-Type': audio.type, ...(scenario ? { 'X-Simulation-Scenario': scenario } : {}) }, body: audio });
}
export async function applyResult(alert: string, result: AssistanceResult, version: number, key: string): Promise<void> {
    await requestJson(`/api/v1/alerts/${encodeURIComponent(alert)}/${kinds[result.kind]}/${encodeURIComponent(result.id)}/apply`, { method: 'POST', headers: { 'Idempotency-Key': key }, body: JSON.stringify({ expectedVersion: version }) });
}
export function supported(source: string, field: SuggestedField): boolean {
    return !field.ambiguous && field.evidence.length > 0 && field.evidence.every(span => Number.isInteger(span.start) && Number.isInteger(span.endExclusive) && span.start >= 0 && span.endExclusive > span.start && span.endExclusive <= source.length)
        && field.value === field.evidence.map(span => source.slice(span.start, span.endExclusive)).join('\n');
}
