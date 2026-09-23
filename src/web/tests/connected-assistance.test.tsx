import React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, test, vi } from 'vitest';
import { AssistancePanel } from '../features/connected/assistance-panel';
import * as assistance from '../lib/assistance';
import * as alerts from '../lib/alerts';
vi.mock('../lib/assistance', async (original) => ({ ...await original<typeof assistance>(), getCapabilities: vi.fn(), getHistory: vi.fn(), generateStructure: vi.fn(), transcribe: vi.fn(), applyResult: vi.fn() }));
vi.mock('../lib/alerts', async (original) => ({ ...await original<typeof alerts>(), getAlertDraft: vi.fn() }));
const draft: alerts.AlertDraft = { alertId: 'sim-alert', draftVersion: 1, state: 'Draft', simulationPatientReference: 'SIM-PAT-1', location: 'Simulation room', urgencyLabel: 'DEMO', sourceType: 'Typed', sourceText: 'SIMULATION: Situation: fictional 82 mmHg', sbar: { situation: 'SIMULATION: manual', background: 'SIMULATION: manual', assessment: 'SIMULATION: manual', recommendation: 'SIMULATION: manual' }, approvedMessage: null, recipients: [], criticalFields: [] };
const result: assistance.AssistanceResult = { id: 'result-1', alertId: 'sim-alert', alertVersion: 1, sourceRevisionId: 'source-1', createdAtUtc: '2026-09-19T12:00:00Z', kind: 'Structuring', provider: 'Simulated', providerVersion: 'DEMO-1', configurationVersion: 'DEMO-1', stale: false, sourceText: draft.sourceText!, transcription: null, suggestion: { fields: [{ path: 'situation', value: 'fictional 82 mmHg', evidence: [{ start: 23, endExclusive: 40 }], confidence: null, ambiguous: false }], missingFields: ['background', 'assessment', 'recommendation'], ambiguities: [], confidence: null, provider: 'Simulated', providerVersion: 'DEMO-1', configurationVersion: 'DEMO-1' } };
function setup(speech = false) {
    vi.mocked(assistance.getCapabilities).mockResolvedValue({ speechTranscription: speech, alertStructuringSuggestions: true, speechProvider: speech ? 'Simulated' : 'Disabled', acceptedAudioContentTypes: speech ? ['audio/webm;codecs=opus'] : [], simulationOnly: true });
    vi.mocked(assistance.getHistory).mockResolvedValue({ items: [], nextCursor: null });
    vi.mocked(assistance.generateStructure).mockResolvedValue(result);
    vi.mocked(alerts.getAlertDraft).mockResolvedValue({ ...draft, draftVersion: 2 });
}
afterEach(() => { vi.resetAllMocks(); vi.unstubAllGlobals(); });
test('all flags disabled leave optional controls absent', async () => {
    setup();
    vi.mocked(assistance.getCapabilities).mockResolvedValue({ speechTranscription: false, alertStructuringSuggestions: false, speechProvider: 'Disabled', acceptedAudioContentTypes: [], simulationOnly: true });
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    await waitFor(() => expect(assistance.getCapabilities).toHaveBeenCalled());
    expect(screen.queryByRole('button', { name: 'Suggest SBAR structure' })).not.toBeInTheDocument();
});
test('generation displays separate evidence and missing confidence without applying', async () => {
    setup();
    const applied = vi.fn();
    render(<AssistancePanel draft={draft} disabled={false} onApplied={applied} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    expect(await screen.findByRole('heading', { name: 'Structured suggestion' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Source used for this suggestion' })).toBeVisible();
    expect(screen.getAllByText('Confidence not provided').length).toBeGreaterThan(0);
    expect(screen.getByText(/Missing information/)).toBeVisible();
    expect(applied).not.toHaveBeenCalled();
    expect(assistance.applyResult).not.toHaveBeenCalled();
});
test('explicit Apply uses the result and version once despite double click', async () => {
    setup();
    let finish!: () => void;
    vi.mocked(assistance.applyResult).mockImplementation(() => new Promise<void>(resolve => { finish = resolve; }));
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    const apply = await screen.findByRole('button', { name: 'Apply evidence-backed suggestion' });
    await waitFor(() => expect(apply).toBeEnabled());
    fireEvent.click(apply);
    fireEvent.click(apply);
    expect(assistance.applyResult).toHaveBeenCalledTimes(1);
    finish();
    await waitFor(() => expect(alerts.getAlertDraft).toHaveBeenCalled());
});
test('stale history cannot apply and unsaved changes block generation', async () => {
    setup();
    vi.mocked(assistance.getHistory).mockResolvedValue({ items: [{ ...result, stale: true }], nextCursor: null });
    render(<AssistancePanel draft={draft} disabled onApplied={vi.fn()} onBusy={vi.fn()}/>);
    expect(await screen.findByRole('button', { name: 'Apply evidence-backed suggestion' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Suggest SBAR structure' })).toBeDisabled();
    expect(screen.getByText(/older source version/)).toBeVisible();
});
test('provider failure offers typing without reflecting raw errors', async () => {
    setup();
    vi.mocked(assistance.generateStructure).mockRejectedValue(new Error('SECRET-PROVIDER-BODY'));
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/continue.*manual/i);
    expect(screen.queryByText(/SECRET-PROVIDER-BODY/)).not.toBeInTheDocument();
});
test('microphone denial leaves a typed fallback', async () => {
    setup(true);
    vi.stubGlobal('MediaRecorder', class {
        static isTypeSupported() { return true; }
    });
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia: vi.fn().mockRejectedValue(new Error('denied')) } });
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Record dictation' }));
    expect(await screen.findByRole('alert')).toHaveTextContent(/Microphone access is unavailable/);
    vi.unstubAllGlobals();
});
test('in-progress and uncertain retries preserve the same operation key', async () => {
    setup();
    vi.mocked(assistance.generateStructure).mockRejectedValueOnce(new alerts.AlertApiError(409, 'operation-in-progress', 'safe'));
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry same assistance request' }));
    await screen.findByRole('heading', { name: 'Structured suggestion' });
    expect(vi.mocked(assistance.generateStructure).mock.calls[0][2]).toBe(vi.mocked(assistance.generateStructure).mock.calls[1][2]);
});
test('unsupported evidence is visible and cannot apply', async () => {
    setup();
    vi.mocked(assistance.generateStructure).mockResolvedValue({ ...result, suggestion: { ...result.suggestion!, fields: [{ ...result.suggestion!.fields[0], value: 'invented recommendation' }] } });
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    expect(await screen.findByText(/Unsupported suggestion/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Apply evidence-backed suggestion' })).toBeDisabled();
});
test('unmount stops microphone tracks and never uploads unfinished recording', async () => {
    setup(true);
    const stop = vi.fn();
    const stopRecorder = vi.fn();
    vi.stubGlobal('MediaRecorder', class {
        static isTypeSupported() { return true; }
        state = 'recording';
        start() { }
        stop() { stopRecorder(); }
    });
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia: vi.fn().mockResolvedValue({ getTracks: () => [{ stop }] }) } });
    const view = render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()}/>);
    fireEvent.click(await screen.findByRole('button', { name: 'Record dictation' }));
    await screen.findByRole('button', { name: 'Stop recording' });
    view.unmount();
    expect(stop).toHaveBeenCalledOnce();
    expect(stopRecorder).toHaveBeenCalledOnce();
    expect(assistance.transcribe).not.toHaveBeenCalled();
});

test('generic 503 retains the operation key for a possibly committed result', async () => {
    setup();
    vi.mocked(assistance.generateStructure).mockRejectedValueOnce(new alerts.AlertApiError(503, null, 'safe'));
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Retry same assistance request' }));
    await screen.findByRole('heading', { name: 'Structured suggestion' });
    expect(vi.mocked(assistance.generateStructure).mock.calls[0][2]).toBe(vi.mocked(assistance.generateStructure).mock.calls[1][2]);
});

test('late initial history cannot erase a result generated after the history request began', async () => {
    setup();
    let finish!: (value: assistance.AssistancePage) => void;
    vi.mocked(assistance.getHistory).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    render(<AssistancePanel draft={draft} disabled={false} onApplied={vi.fn()} onBusy={vi.fn()} />);
    fireEvent.click(await screen.findByRole('button', { name: 'Suggest SBAR structure' }));
    await screen.findByRole('heading', { name: 'Structured suggestion' });
    await act(async () => { finish({ items: [], nextCursor: null }); });
    expect(screen.getByRole('heading', { name: 'Structured suggestion' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Apply evidence-backed suggestion' })).toBeEnabled();
});
