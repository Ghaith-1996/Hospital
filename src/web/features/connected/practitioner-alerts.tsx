"use client";
import React from "react";
import Link from "next/link";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading, useServerQuery } from "./common";
import { useIdempotentAction } from "./use-idempotent-action";

export function ResponseFacts({ alert }: { alert: Pick<api.MyAlertSummary, "acknowledgedAtUtc" | "terminalDisposition" | "responsibilityAcceptedAtUtc" | "callUnitRequestedAtUtc"> }) {
  return <div className="response-summary__grid"><p>Acknowledged: {alert.acknowledgedAtUtc ?? "Not recorded"}</p><p>Terminal disposition: {alert.terminalDisposition ?? "Not recorded"}</p><p>Responsibility accepted: {alert.responsibilityAcceptedAtUtc ?? "Not recorded"}</p><p>Call to unit requested: {alert.callUnitRequestedAtUtc ?? "Not recorded"}</p></div>;
}
export function PractitionerInbox() {
  const query = useServerQuery(api.getMyAlerts);
  return <section className="doctor-inbox"><PageHeader title="My Alerts" description="Only alerts addressed to your backend-linked fictional practitioner appear here." actions={<button type="button" className="button-secondary" onClick={query.reload}>Refresh inbox</button>} />
    <ApiError error={query.error} retry={query.reload} />{query.loading ? <Loading /> : query.data && <div className="doctor-inbox__list">{query.data.length === 0 && <p>No active alerts addressed to this practitioner.</p>}{query.data.map(alert => <article className="doctor-inbox-card" key={alert.alertId}>
      <h2><Link className="doctor-inbox__alert-link" href={`/my-alerts/${alert.alertId}`}>Open alert {alert.alertId}</Link></h2><p>{alert.location} · {alert.urgencyLabel} · {alert.state}</p><p>Channels: {alert.channels.join(", ")} · Opened: {alert.openedState}</p><ResponseFacts alert={alert} />
    </article>)}</div>}
  </section>;
}
export function PractitionerAlert({ alertId }: { alertId: string }) {
  const load = React.useCallback(() => api.getMyAlert(alertId), [alertId]);
  const query = useServerQuery(load);
  const refresh = query.refresh;
  const action = useIdempotentAction(refresh);
  const alert = query.data;
  const choices: Array<{ type: api.RecipientResponseType; label: string; reason: api.RecipientResponseReasonCode; done: boolean }> = alert ? [
    { type: "Acknowledged", label: "Acknowledge", reason: "simulation-acknowledged", done: !!alert.acknowledgedAtUtc },
    { type: "Accepted", label: "Accept responsibility", reason: "simulation-responsibility-accepted", done: !!alert.terminalDisposition },
    { type: "Declined", label: "Decline", reason: "simulation-declined", done: !!alert.terminalDisposition },
    { type: "Unavailable", label: "Unavailable", reason: "simulation-unavailable", done: !!alert.terminalDisposition },
    { type: "CallUnitRequested", label: "Request call to unit", reason: "simulation-call-unit-requested", done: !!alert.callUnitRequestedAtUtc },
  ] : [];
  return <div className="doctor-alert"><PageHeader title="Doctor Alert" description="Opening, acknowledgement and responsibility acceptance are distinct simulation actions." /><Link href="/my-alerts">Back to inbox</Link>
    <ApiError error={query.error} retry={query.reload} /><ApiError error={action.error} retry={() => void action.refresh()} />
    {query.loading ? <Loading /> : alert && <><div className="doctor-alert__grid">
      <section className="doctor-alert__card"><h2>Alert details</h2><p>{alert.simulationPatientReference}</p><p>{alert.location}</p><p>{alert.urgencyLabel}</p><p>{alert.state} · Confirmed version {alert.confirmedVersion}</p></section>
      <section className="doctor-alert__card doctor-alert__card--case"><h2>Approved secure message</h2><p>{alert.approvedMessage}</p>{alert.criticalFields.map(field => <p key={field.fieldId}>{field.fieldId}: {field.value} {field.unit}</p>)}</section>
      <section className="doctor-alert__card"><h2>Opening</h2><p>Opened: {alert.openedState}</p><p>{alert.secureMessageOpenedAtUtc ?? "Opening not recorded"}</p><button type="button" disabled={action.busy || action.refreshRequired || !alert.channels.includes("SecureMessage") || !!alert.secureMessageOpenedAtUtc || (!!action.uncertain && action.uncertain !== "Record opened")} onClick={() => void action.execute("Record opened", key => api.markMyAlertOpened(alertId, alert.confirmedVersion, key))}>Record opened</button></section>
    </div><section className="detail-card"><h2>Your durable response</h2><ResponseFacts alert={alert} /><p>Acknowledgement does not accept responsibility. Acceptance does not resolve the alert.</p></section>
      <section className="response-panel"><h2>Respond to alert</h2>{action.uncertain && <p role="status">Outcome uncertain. Retry {action.uncertain} using the same attempt.</p>}<div className="form-actions">{choices.map(choice => <button className="response-action" type="button" key={choice.type} disabled={action.busy || action.refreshRequired || choice.done || (!!action.uncertain && action.uncertain !== choice.label)} onClick={() => void action.execute(choice.label, key => api.recordMyAlertResponse(alertId, alert.confirmedVersion, choice.type, key, choice.reason))}>{choice.label}</button>)}</div></section>
    </>}
  </div>;
}
