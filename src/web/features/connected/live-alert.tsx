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
  const sequence = React.useRef({ value: 0 });
  const refresh = React.useCallback(async () => {
    const current = ++sequence.current.value;
    try { const value = await api.getAlertLive(alertId); if (current === sequence.current.value) { setLive(value); setError(null); } }
    catch (failure) { if (current === sequence.current.value) setError(failure); throw failure; }
  }, [alertId]);
  React.useEffect(() => {
    const counter = sequence.current;
    void refresh().catch(() => {});
    const interval = pollMs > 0 ? window.setInterval(() => void refresh().catch(() => {}), pollMs) : undefined;
    return () => { ++counter.value; if (interval) window.clearInterval(interval); };
  }, [refresh, pollMs]);
  const action = useIdempotentAction(refresh);
  return <div className="alert-details-page"><PageHeader title="Alert Live Status" description="Refreshed durable simulation status. Delivery, opening, acknowledgement and responsibility remain separate." actions={<button type="button" className="button-secondary" onClick={() => void refresh().catch(() => {})}>Refresh status</button>} />
    <ApiError error={error} retry={() => void refresh().catch(() => {})} /><ApiError error={action.error} retry={() => void action.refresh()} />
    {action.uncertain && <button type="button" className="button-secondary" disabled={action.busy} onClick={() => void action.retry()}>Retry {action.uncertain} action</button>}
    {!live && !error && <Loading />}{live && <>
      <section aria-label="Operational warnings">{Array.isArray(live.operationalWarnings) && live.operationalWarnings.slice(0, 8).map(warning => {
        const message = warning && typeof warning.code === "string" && Object.hasOwn(api.operationalWarningMessages, warning.code) ? api.operationalWarningMessages[warning.code] : null;
        return message ? <div className="error-panel" key={warning.code}><h2>{message[0]}</h2><p>{message[1]}</p></div> : null;
      })}</section>
      <section className="detail-card"><h2>{live.alertState}</h2><p>Alert {live.alertId} · Confirmed version {live.confirmedVersion}</p><p>Outbox: {live.outboxState}</p><p>Last server refresh: {live.refreshedAtUtc}</p>{!!error && <p>Showing the last successful response; current state is unavailable.</p>}</section>
      {live.manualFallbackRequired && <section className="error-panel"><h2>Manual fallback required</h2><p>REQUIRES_HOSPITAL_DECISION — a hospital-approved fallback procedure is required. No contact route is configured.</p></section>}
      <section className="detail-card"><h2>DEMO escalation</h2>{live.escalation ? <>
        <p>Policy {live.escalation.policyVersion} · {live.escalation.policyId}</p>
        <p>Escalation state: {live.escalation.state} · Step {live.escalation.currentStep}</p>
        {live.escalation.nextDueAtUtc && <p>Next due (UTC): {live.escalation.nextDueAtUtc}</p>}
        {live.escalation.remainingDelaySeconds !== null && <p>Paused delay remaining: {Math.ceil(live.escalation.remainingDelaySeconds)} seconds</p>}
        {live.escalation.stopReason && <p>Stop reason: {live.escalation.stopReason.replace(/([a-z])([A-Z])/g, "$1 $2")}</p>}
        <p>Simulation only. Production timing, authority and fallback: REQUIRES_HOSPITAL_DECISION.</p>
        <div className="form-actions">{live.escalation.canPause && <button type="button" className="button-secondary" disabled={action.busy || action.refreshRequired || (!!action.uncertain && action.uncertain !== "Pause")} onClick={() => void action.execute("Pause", key => api.setEscalationPaused(alertId, live.confirmedVersion, true, key))}>Pause DEMO escalation</button>}
          {live.escalation.canResume && <button type="button" disabled={action.busy || action.refreshRequired || (!!action.uncertain && action.uncertain !== "Resume")} onClick={() => void action.execute("Resume", key => api.setEscalationPaused(alertId, live.confirmedVersion, false, key))}>Resume DEMO escalation</button>}</div>
        <h3>Escalation timeline</h3><ol>{live.escalation.events.map(event => <li key={event.sequence}>{event.kind.replace(/([a-z])([A-Z])/g, "$1 $2")} · Step {event.step} · <time dateTime={event.occurredAtUtc}>{event.occurredAtUtc}</time></li>)}</ol>
      </> : <p>Escalation disabled: no exact escalation plan was approved for this alert version.</p>}</section>
      {live.recipients.map(person => <section className="detail-card" key={person.practitionerId}><h2>{person.displayName}</h2><p>{person.simulationCode} · {person.specialty} · {person.onCallSnapshot ?? "No on-call evidence"}</p>
        {person.selectionSources?.includes("EscalationPolicy") && <p>Activated from the approved DEMO escalation plan</p>}
        <ResponseFacts alert={person} /><div className="detail-grid">{person.attempts.length === 0 && <p>No delivery attempts recorded yet.</p>}{person.attempts.map(attempt => <article className="detail-card" key={`${attempt.channel}-${attempt.attemptNumber}`}><h3>{attempt.channel} · Attempt {attempt.attemptNumber}</h3><p>Status: {attempt.status}</p><p>Requested: {attempt.requestedAtUtc}</p><p>Submitted/provider accepted: {attempt.submittedAtUtc ?? "Not observed"}</p><p>Delivered: {attempt.deliveredAtUtc ?? "Not observed"}</p><p>Failed: {attempt.failedAtUtc ?? "Not observed"}</p><p>Opened: {attempt.openedState} {attempt.openedAtUtc ?? ""}</p>{attempt.failureCategory && <p role="alert">{attempt.failureCategory}</p>}</article>)}</div>
      </section>)}
      <div className="form-actions">{action.uncertain && <p>Outcome uncertain. Retry the same lifecycle action.</p>}{live.canResolve && <button type="button" disabled={action.busy || action.refreshRequired || (!!action.uncertain && action.uncertain !== "Resolve")} onClick={() => void action.execute("Resolve", key => api.resolveAlert(alertId, live.confirmedVersion, key))}>Resolve simulation alert</button>}{live.canCancel && <button type="button" className="button-secondary" disabled={action.busy || action.refreshRequired || (!!action.uncertain && action.uncertain !== "Cancel")} onClick={() => void action.execute("Cancel", key => api.cancelAlert(alertId, live.confirmedVersion, key))}>Cancel simulation alert</button>}</div>
    </>}
  </div>;
}

