"use client";
import React from "react";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading } from "./common";
import { ResponseFacts } from "./practitioner-alerts";
import { useIdempotentAction } from "./use-idempotent-action";

export function LiveAlert({ alertId, pollMs = 5000 }: { alertId: string; pollMs?: number }) {
  // Route changes get fresh query/action state; cached evidence belongs to one alert only.
  return <LiveAlertContent key={alertId} alertId={alertId} pollMs={pollMs} />;
}

function LiveAlertContent({ alertId, pollMs }: { alertId: string; pollMs: number }) {
  const [live, setLive] = React.useState<api.AlertLive | null>(null);
  const [error, setError] = React.useState<unknown>(null);
  const [pauseReason, setPauseReason] = React.useState<api.EscalationPauseReason>("OperatorReview");
  const sequence = React.useRef({ value: 0 });
  const refresh = React.useCallback(async () => {
    const current = ++sequence.current.value;
    try {
      const value = await api.getAlertLive(alertId);
      if (value.alertId !== alertId) throw new Error("Unexpected alert identity in server response.");
      if (current === sequence.current.value) { setLive(value); setError(null); }
    }
    catch (failure) { if (current === sequence.current.value) setError(failure); throw failure; }
  }, [alertId]);
  React.useEffect(() => {
    const counter = sequence.current;
    void refresh().catch(() => {});
    const interval = pollMs > 0 ? window.setInterval(() => void refresh().catch(() => {}), pollMs) : undefined;
    return () => { ++counter.value; if (interval) window.clearInterval(interval); };
  }, [refresh, pollMs]);
  const action = useIdempotentAction(refresh);
  const blocked = action.busy || action.refreshRequired || !!action.uncertain || !!error;
  return <div className="alert-details-page"><PageHeader title="Alert Live Status" description="Refreshed durable simulation status. Delivery, opening, acknowledgement and responsibility remain separate." actions={<button type="button" className="button-secondary" onClick={() => void refresh().catch(() => {})}>Refresh status</button>} />
    <ApiError error={error} retry={() => void refresh().catch(() => {})} /><ApiError error={action.error} retry={() => void action.refresh()} />
    {!live && !error && <Loading />}{live && <>
      <section className="detail-card"><h2>{live.alertState}</h2><p>Alert {live.alertId} · Confirmed version {live.confirmedVersion}</p><p>Outbox: {live.outboxState}</p><p>Last server refresh: {live.refreshedAtUtc}</p>{!!error && <p>Showing the last successful response; current state is unavailable.</p>}</section>
      {(live.manualFallbackRequired || live.escalation.manualFallbackRequired) && <section className="error-panel"><h2>Manual fallback required</h2><p>REQUIRES_HOSPITAL_DECISION — a hospital-approved fallback procedure is required. No contact route is configured.</p></section>}
      <section className="detail-card" aria-labelledby="escalation-heading">
        <h2 id="escalation-heading">DEMO escalation</h2>
        {!live.escalation.automaticEscalationEligible ? <p>Automatic escalation is unavailable: this alert has no eligible exact confirmed plan.</p> : <>
          <p>Policy: {live.escalation.policyVersion} · {live.escalation.policyId}</p>
          <p>Escalation status: {live.escalation.runState ?? "Waiting for scheduling"}</p>
          {live.escalation.nextStepSequence !== null && <p>Next approved step: {live.escalation.nextStepSequence} of {live.escalation.totalSteps}</p>}
          {live.escalation.nextEvaluationAtUtc && <p>Next evaluation (UTC): {live.escalation.nextEvaluationAtUtc}</p>}
          {live.escalation.paused && <p>Remaining delay: {live.escalation.remainingPauseSeconds} seconds. Timing resumes after an authorized Resume.</p>}
          {live.escalation.exhausted && <p>All approved automatic steps have been queued. Their delivery and responsibility status are shown separately below.</p>}
          {live.escalation.terminalOutcome && <p>Outcome: {readable(live.escalation.terminalOutcome)}</p>}
          {live.escalation.reasonCode && <p>Reason: {readable(live.escalation.reasonCode)}</p>}
          {live.escalation.failureCategory && <p role="alert">Escalation failure: {readable(live.escalation.failureCategory)}</p>}
          <p>Escalation delivery queue: {live.escalation.escalationOutboxState}</p>
          {live.escalation.escalationOutboxFailureCategory && <p role="alert">Escalation delivery failure: {readable(live.escalation.escalationOutboxFailureCategory)}</p>}
          <p>The server manages escalation timing. Pause suspends future step activation; already queued notifications can still complete.</p>
        </>}
        <div className="form-actions">
          {live.escalation.canPause && <><label>Pause reason <select value={pauseReason} disabled={blocked} onChange={event => setPauseReason(event.target.value as api.EscalationPauseReason)}><option value="OperatorReview">Operator review</option><option value="ManualCoordination">Manual coordination</option></select></label><button type="button" className="button-secondary" disabled={blocked} onClick={() => void action.execute("Pause", key => api.pauseEscalation(alertId, live.confirmedVersion, pauseReason, key))}>Pause escalation</button></>}
          {live.escalation.canResume && <button type="button" disabled={blocked} onClick={() => void action.execute("Resume", key => api.resumeEscalation(alertId, live.confirmedVersion, key))}>Resume escalation</button>}
        </div>
        <h3>Escalation timeline</h3>
        {live.escalation.timeline.length === 0 ? <p>No escalation events recorded.</p> : <ol>{live.escalation.timeline.map(event => <li key={event.eventId}><p><time dateTime={event.occurredAtUtc}>{event.occurredAtUtc}</time> · Step {event.stepSequence} · {readable(event.eventType)}{event.overrideReason && ` · ${readable(event.overrideReason)}`}{event.failureCategory && ` · ${readable(event.failureCategory)}`}</p></li>)}</ol>}
      </section>
      {live.recipients.map(person => <section className="detail-card" key={person.practitionerId}><h2>{person.displayName}</h2><p>{person.simulationCode} · {person.specialty} · {person.onCallSnapshot ?? "No on-call evidence"}</p>
        {person.selections.map(selection => <p key={selection.selectionId}>{selection.channel} · {selection.selectionSource === "EscalationPolicy" && selection.escalationStepSequence !== null ? `Added by confirmed DEMO escalation policy ${selection.escalationPolicyVersion}, step ${selection.escalationStepSequence}.` : `Selection source: ${readable(selection.selectionSource)}`}</p>)}
        <ResponseFacts alert={person} /><div className="detail-grid">{person.attempts.length === 0 && <p>No delivery attempts recorded yet.</p>}{person.attempts.map(attempt => <article className="detail-card" key={`${attempt.channel}-${attempt.attemptNumber}`}><h3>{attempt.channel} · Attempt {attempt.attemptNumber}</h3><p>Status: {attempt.status}</p><p>Requested: {attempt.requestedAtUtc}</p><p>Submitted/provider accepted: {attempt.submittedAtUtc ?? "Not observed"}</p><p>Delivered: {attempt.deliveredAtUtc ?? "Not observed"}</p><p>Failed: {attempt.failedAtUtc ?? "Not observed"}</p><p>Opened: {attempt.openedState} {attempt.openedAtUtc ?? ""}</p>{attempt.failureCategory && <p role="alert">{attempt.failureCategory}</p>}</article>)}</div>
      </section>)}
      <div className="form-actions">{action.uncertain && <><p>Outcome uncertain. Retry the original request before another action.</p><button type="button" disabled={action.busy || action.refreshRequired} onClick={() => void action.retry()}>Retry {action.uncertain} request</button></>}{live.canResolve && <button type="button" disabled={blocked} onClick={() => void action.execute("Resolve", key => api.resolveAlert(alertId, live.confirmedVersion, key))}>Resolve simulation alert</button>}{live.canCancel && <button type="button" className="button-secondary" disabled={blocked} onClick={() => void action.execute("Cancel", key => api.cancelAlert(alertId, live.confirmedVersion, key))}>Cancel simulation alert</button>}</div>
    </>}
  </div>;
}

function readable(value: string) {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/-/g, " ");
}

