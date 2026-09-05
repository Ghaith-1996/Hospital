"use client";
import React from "react";
import { PageHeader } from "../../components/ui/page-header";
import { applyDirectoryImport, previewDirectoryImport, type DirectoryImportPreview } from "../../lib/directory-import";
import { ApiError, useUnsavedChanges } from "./common";
export function DirectoryImport() {
  const [file, setFile] = React.useState<File | null>(null);
  const [preview, setPreview] = React.useState<DirectoryImportPreview | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  const [result, setResult] = React.useState<string | null>(null);
  const lock = React.useRef(false);
  const releaseDirty = useUnsavedChanges(!!file && !result);
  async function run(apply: boolean) {
    if (lock.current || !file || (apply && (!preview?.previewToken || preview.errors.length))) return;
    lock.current = true; setBusy(true); setError(null);
    try {
      if (apply) {
        const response = await applyDirectoryImport(file, preview!.previewToken);
        setPreview(response.preview);
        if (response.applied) { releaseDirty(); setResult(`Import applied. Sync run ${response.syncRunId}`); setPreview(null); }
      } else setPreview(await previewDirectoryImport(file));
    } catch (failure) { setError(failure); if (apply) setPreview(null); }
    finally { lock.current = false; setBusy(false); }
  }
  return <><PageHeader title="Directory Import" description="Preview and reconcile the fictional CSV fixture using the server directory adapter." /><section className="new-alert-form">
    <p>Fictional SIM- records and 555 endpoints only. Directory-administrator authorization is enforced by the server.</p><ApiError error={error} />
    <label className="filter-field">Simulation CSV<input type="file" accept=".csv,text/csv" disabled={busy} onChange={event => { setFile(event.target.files?.[0] ?? null); setPreview(null); setError(null); setResult(null); }} /></label>
    <div className="form-actions"><button type="button" disabled={!file || busy} onClick={() => void run(false)}>Preview import</button><button type="button" disabled={!preview?.previewToken || preview.errors.length > 0 || busy} onClick={() => void run(true)}>Apply import</button><button type="button" className="button-secondary" disabled={busy} onClick={() => { setFile(null); setPreview(null); setResult(null); }}>Discard selected file</button></div>
    {result && <p role="status">{result}</p>}{preview && <section className="detail-card"><h2>Preview ready for {preview.sourceSystem}</h2><p>{preview.parsedPractitionerCount} practitioners · {preview.insertCount} insert · {preview.updateCount} update · {preview.rejectedCount} rejected</p>{preview.errors.map((issue, i) => <p role="alert" key={i}>{issue.code} · row {issue.rowNumber}: {issue.message}</p>)}{preview.warnings.map((issue, i) => <p key={i}>Warning: {issue.code} · row {issue.rowNumber}: {issue.message}</p>)}{preview.changes.map((change, i) => <p key={i}>{change.action} · {change.simulationCode} · {change.displayName} · {change.selectable ? "Selectable" : "Not selectable"}</p>)}</section>}
  </section></>;
}
