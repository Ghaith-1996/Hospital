"use client";
import React from "react";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading } from "./common";
import { ResponseFacts } from "./practitioner-alerts";
import { useIdempotentAction } from "./use-idempotent-action";

export function LiveAlert({ alertId, pollMs = 5000 }: { alertId: string; pollMs?: number }) {
  const [live, setLive] = React.useState<api.AlertLive | null>(null);
  const [error, setError] = React.useState<unknown>(null);
  const sequence = React.useRef(0);
  const refresh = React.useCallback(async () => {
    const current = ++sequence.current;
    try { const value = await api.getAlertLive(alertId); if (current === sequence.current) { setLive(value); setError(null); } }
    catch (failure) { if (current === sequence.current) setError(failure); }
  }, [alertId]);
  React.useEffect(() => {
    void refresh();
    const interval = pollMs > 0 ? window.setInterval(() => void refresh(), pollMs) : undefined;
    return () => { ++sequence.current; if (interval) window.clearInterval(interval); };
  }, [refresh, pollMs]);
  const action = useIdempotentAction(refresh);
  return <div className="alert-details-page"><PageHeader title="Alert Live Status" description="Refreshed durable simulation status. Delivery, opening, acknowledgement and responsibility remain separate." actions={<button type="button" className="button-secondary" onClick={() => void refresh()}>Refresh status</button>} />
    <ApiError error={error} retry={() => void refresh()} /><ApiError error={action.error} retry={() => void refresh()} />
    {!live && !error && <Loading />}{live && <>
      <section className="detail-card"><h2>{live.alertState}</h2><p>Alert {live.alertId} · Confirmed version {live.confirmedVersion}</p><p>Outbox: {live.outboxState}</p><p>Last server refresh: {live.refreshedAtUtc}</p>{!!error && <p>Showing the last successful response; current state is unavailable.</p>}</section>
      {live.manualFallbackRequired && <section className="error-panel"><h2>Manual fallback required</h2><p>REQUIRES_HOSPITAL_DECISION — a hospital-approved fallback procedure is required. No contact route is configured.</p></section>}
      {live.recipients.map(person => <section className="detail-card" key={person.practitionerId}><h2>{person.displayName}</h2><p>{person.simulationCode} · {person.specialty} · {person.onCallSnapshot ?? "No on-call evidence"}</p>
        <ResponseFacts alert={person} /><div className="detail-grid">{person.attempts.length === 0 && <p>No delivery attempts recorded yet.</p>}{person.attempts.map(attempt => <article className="detail-card" key={`${attempt.channel}-${attempt.attemptNumber}`}><h3>{attempt.channel} · Attempt {attempt.attemptNumber}</h3><p>Status: {attempt.status}</p><p>Requested: {attempt.requestedAtUtc}</p><p>Submitted/provider accepted: {attempt.submittedAtUtc ?? "Not observed"}</p><p>Delivered: {attempt.deliveredAtUtc ?? "Not observed"}</p><p>Failed: {attempt.failedAtUtc ?? "Not observed"}</p><p>Opened: {attempt.openedState} {attempt.openedAtUtc ?? ""}</p>{attempt.failureCategory && <p role="alert">{attempt.failureCategory}</p>}</article>)}</div>
      </section>)}
      <div className="form-actions">{action.uncertain && <p>Outcome uncertain. Retry the same lifecycle action.</p>}{live.canResolve && <button type="button" disabled={action.busy || (!!action.uncertain && action.uncertain !== "Resolve")} onClick={() => void action.execute("Resolve", key => api.resolveAlert(alertId, live.confirmedVersion, key))}>Resolve simulation alert</button>}{live.canCancel && <button type="button" className="button-secondary" disabled={action.busy || (!!action.uncertain && action.uncertain !== "Cancel")} onClick={() => void action.execute("Cancel", key => api.cancelAlert(alertId, live.confirmedVersion, key))}>Cancel simulation alert</button>}</div>
    </>}
  </div>;
}

