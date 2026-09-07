"use client";
import React from "react";
import Link from "next/link";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading, useServerQuery } from "./common";

export function ReviewAlert({ alertId }: { alertId: string }) {
  const load = React.useCallback(() => api.getAlertReview(alertId), [alertId]);
  const query = useServerQuery(load);
  return <><PageHeader title="Exact Review" description="Confirm only the exact message, values, recipients and channels returned by the server." /><ApiError error={query.error} retry={query.reload} />
    {query.error && <Link href={`/alerts/${alertId}/compose`}>Return to compose and review prerequisites</Link>}
    {query.loading ? <Loading /> : query.data && <ExactReview key={query.data.draftVersion} review={query.data} reload={query.reload} />}</>;
}
function ExactReview({ review, reload }: { review: api.AlertReview; reload(): void }) {
  const [checked, setChecked] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [attempted, setAttempted] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  const [result, setResult] = React.useState<api.ConfirmResult | null>(null);
  const [conflict, setConflict] = React.useState(false);
  const attempt = React.useRef<string | null>(null);
  const lock = React.useRef(false);
  if (result) return <section className="sent-success"><h2>DispatchQueued</h2><p>The server persisted the dispatch request for the simulation worker. Delivery has not been inferred from confirmation.</p><Link className="button-primary" href={`/alerts/${review.alertId}/live`}>Open live status</Link></section>;
  return <div className="review-page"><p>Draft version {review.draftVersion} · {review.state}</p><ApiError error={error} retry={conflict ? reload : undefined} />
    <section className="detail-card"><h2>Approved alert</h2><p>{review.simulationPatientReference} · {review.location} · {review.urgencyLabel}</p><p className="case-copy">{review.approvedMessage}</p></section>
    <section className="detail-card"><h2>Critical fields</h2>{review.criticalFields.map(field => <p key={field.fieldId}>{field.fieldId}: original {field.originalValue} · approved {field.normalizedValue} · unit {field.unit ?? "Missing"} · {field.status}</p>)}</section>
    <section className="detail-card"><h2>Exact recipients and channels</h2>{review.recipients.map((person, index) => <article className="detail-card" key={`${person.practitionerId}-${index}`}><h3>{person.displayName}</h3><p>{person.specialty} · {person.department} · {person.site} · {person.roleTitle}</p><p>Channel: {person.channel} · Source: {person.selectionSource}</p><p>{person.isStale ? "Stale" : "Current"} · Directory synchronized: {person.directorySourceUpdatedAtUtc ?? "Unknown"}</p><p>On-call evidence: {person.onCallSnapshot ?? "Not available"}</p><p>Selected: {person.selectedAtUtc} · Revision: {person.directoryRevision}</p></article>)}</section>
    <section className="detail-card"><h2>Policy versions</h2><p>Notification: {review.demoNotificationPolicyVersion}</p><p>Escalation reference: {review.demoEscalationPolicyVersion}</p><p>Simulation only; no automated escalation runs.</p></section>
    <label className="confirmation-check"><input type="checkbox" checked={checked} disabled={busy || attempted} onChange={event => setChecked(event.target.checked)} />I reviewed the exact version, message, values, units, recipients, channels and policy versions.</label>
    <div className="form-actions"><Link href={`/alerts/${review.alertId}/compose`}>Edit and reconfirm</Link><Link href={`/alerts/${review.alertId}/recipients`}>Review recipients</Link>
      <button type="button" disabled={!checked || busy || conflict} onClick={async () => {
        if (lock.current) return; lock.current = true; setBusy(true); setError(null);
        attempt.current ??= api.createIdempotencyKey(); setAttempted(true);
        try { setResult(await api.confirmAlertReview(review.alertId, review.draftVersion, attempt.current)); }
        catch (failure) { setError(failure); if (api.isAlertApiError(failure) && failure.status !== 429 && failure.status < 500) setConflict(true); }
        finally { lock.current = false; setBusy(false); }
      }}>{busy ? "Confirming…" : attempted ? "Retry same confirmation" : "Confirm & Dispatch"}</button>
    </div>
  </div>;
}
