"use client";
import React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Field, Loading, useServerQuery, useUnsavedChanges } from "./common";
import { DraftFields, type DraftContent } from "./draft-fields";

export function ComposeAlert({ alertId }: { alertId: string }) {
  const load = React.useCallback(() => api.getAlertDraft(alertId), [alertId]);
  const query = useServerQuery(load);
  const [conflict, setConflict] = React.useState<unknown>(null);
  return <><PageHeader title="Compose Alert" description="Review source, SBAR, critical fields and the secure message." />
    <ApiError error={query.error} retry={query.reload} />
    <ApiError error={conflict} />
    {query.loading ? <Loading /> : query.data && <ComposeForm key={`${alertId}:${query.data.draftVersion}`} draft={query.data} onSaved={query.setData} onConflict={setConflict} reload={query.reload} />}
  </>;
}

function contentFrom(draft: api.AlertDraft): DraftContent {
  return { location: draft.location, urgencyLabel: draft.urgencyLabel, sourceText: draft.sourceText ?? "", sbar: draft.sbar ?? { situation: "", background: "", assessment: "", recommendation: "" }, criticalFields: draft.criticalFields.map(field => ({ fieldId: field.fieldId, originalValue: field.originalValue, unit: field.unit ?? "" })) };
}
function ComposeForm({ draft, onSaved, onConflict, reload }: { draft: api.AlertDraft; onSaved(value: api.AlertDraft): void; onConflict(error: unknown): void; reload(): void }) {
  const [content, setContent] = React.useState(() => contentFrom(draft));
  const [message, setMessage] = React.useState(draft.approvedMessage ?? "");
  const [normalized, setNormalized] = React.useState<Record<string, string>>({});
  const [error, setError] = React.useState<unknown>(null);
  const [busy, setBusy] = React.useState(false);
  const lock = React.useRef(false);
  const router = useRouter();
  const contentDirty = JSON.stringify(content) !== JSON.stringify(contentFrom(draft));
  const messageDirty = message !== (draft.approvedMessage ?? "");
  const releaseDirty = useUnsavedChanges(contentDirty || messageDirty || Object.keys(normalized).length > 0);
  async function command(run: () => Promise<api.AlertDraft>, next?: () => void) {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError(null); onConflict(null);
    try { const value = await run(); releaseDirty(); onSaved(value); next?.(); }
    catch (failure) {
      setError(failure);
      if (api.isAlertApiError(failure) && failure.status === 409) {
        setError(null); onConflict(failure);
        try {
          const current = await api.getAlertDraft(draft.alertId);
          // Keep the guidance while replacing the form with the current server version.
          setContent(contentFrom(current)); setMessage(current.approvedMessage ?? ""); setNormalized({});
          releaseDirty(); onSaved(current);
        } catch { /* Preserve conflict and offer explicit reload. */ }
      }
    } finally { lock.current = false; setBusy(false); }
  }
  const editable = draft.state === "Draft" || draft.state === "PendingConfirmation";
  if (!editable) return <section className="detail-card"><p>{draft.state}: this confirmed alert cannot be edited.</p><Link href={`/alerts/${draft.alertId}/live`}>View live status</Link></section>;
  return <div className="new-alert-layout">
    <div className="new-alert-form">
      <p>Draft version {draft.draftVersion} · {draft.state}</p>
      <ApiError error={error} retry={reload} />
      <form onSubmit={event => { event.preventDefault(); void command(() => api.updateAlertDraft(draft.alertId, { ...content, expectedVersion: draft.draftVersion })); }}>
        <fieldset disabled={busy}><DraftFields value={content} onChange={setContent} /><div className="form-actions"><button type="submit" disabled={!contentDirty || messageDirty}>Save source and SBAR</button></div></fieldset>
      </form>
      <section className="detail-card"><h2>Approved Message</h2><Field label="Approved secure message" multiline value={message} onChange={setMessage} />
        <button type="button" disabled={busy || contentDirty || !messageDirty || !message.trim()} onClick={() => void command(() => api.setApprovedMessage(draft.alertId, draft.draftVersion, message))}>Approve and save message</button>
        <p>Saving the approved message creates a new version. Confirm the critical values again after message or recipient changes.</p>
      </section>
      <section className="detail-card"><h2>Explicit Critical Field Confirmation</h2>
        {draft.criticalFields.length === 0 && <p>No critical fields recorded.</p>}
        {draft.criticalFields.map(field => <div className="detail-card" key={field.fieldId}>
          <h3>{field.fieldId}</h3><p>Original value: {field.originalValue} · Unit: {field.unit ?? "Missing unit"}</p><p>{field.status}</p>
          <Field label={`Approved value for ${field.fieldId}`} value={normalized[field.fieldId] ?? field.normalizedValue} onChange={value => setNormalized({ ...normalized, [field.fieldId]: value })} />
          <button type="button" disabled={busy || contentDirty || messageDirty || field.status === "Confirmed"} onClick={() => void command(() => api.confirmCriticalField(draft.alertId, { expectedVersion: draft.draftVersion, fieldId: field.fieldId, originalValue: field.originalValue, normalizedValue: normalized[field.fieldId] ?? field.normalizedValue, unit: field.unit }), () => setNormalized({}))}>Confirm {field.fieldId} value and unit</button>
        </div>)}
      </section>
      <div className="form-actions"><button type="button" className="button-secondary" disabled={busy} onClick={() => { setContent(contentFrom(draft)); setMessage(draft.approvedMessage ?? ""); setNormalized({}); }}>Discard unsaved edits</button>
        <button type="button" disabled={busy || contentDirty || messageDirty} onClick={() => void command(() => api.submitAlertDraft(draft.alertId, draft.draftVersion), () => router.push(`/alerts/${draft.alertId}/review`))}>Submit for exact review</button></div>
    </div>
    <aside className="alert-summary"><h2>Alert Summary</h2><p>{draft.simulationPatientReference}</p><p>{draft.location}</p><p>{draft.urgencyLabel}</p><p>{draft.recipients.length} selected recipient channels</p><Link className="button-secondary" href={`/alerts/${draft.alertId}/recipients`}>Select recipients and channels</Link><p>Recipient edits invalidate critical confirmations. Return here to confirm the new version.</p></aside>
  </div>;
}
