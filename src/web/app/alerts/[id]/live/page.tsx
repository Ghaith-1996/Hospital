"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { SimulationChrome } from "../../../simulation-chrome";
import {
  AlertLive,
  cancelAlert,
  createIdempotencyKey,
  getAlertLive,
  isAlertApiError,
  resolveAlert,
} from "../../../../lib/alerts";

const pollIntervalMilliseconds = 5_000;

function routeAlertId(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function safeUtc(value: string | null): string {
  if (!value) return "not recorded";
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? "not recorded" : parsed.toISOString();
}

function liveError(error: unknown): string {
  if (isAlertApiError(error) && error.status === 401) return "Sign in with a seeded Operator or Administrator identity.";
  if (isAlertApiError(error) && error.status === 403) return "This identity cannot view operator live status.";
  if (isAlertApiError(error) && error.status === 404) return "This simulation alert is not available in the authenticated organization.";
  return "Refreshed status could not be loaded. The previous display may be stale.";
}

export default function AlertLivePage() {
  const params = useParams<{ id: string | string[] }>();
  const alertId = routeAlertId(params.id);
  const [live, setLive] = useState<AlertLive | null>(null);
  const [status, setStatus] = useState("Loading refreshed simulation status.");
  const [pendingAction, setPendingAction] = useState<"resolve" | "cancel" | null>(null);

  useEffect(() => {
    let active = true;
    async function refresh() {
      try {
        const result = await getAlertLive(alertId);
        if (active) {
          setLive(result);
          setStatus(`Refreshed status at ${safeUtc(result.refreshedAtUtc)}`);
        }
      } catch (error: unknown) {
        if (active) setStatus(liveError(error));
      }
    }

    void refresh();
    const intervalId = globalThis.setInterval(() => void refresh(), pollIntervalMilliseconds);
    return () => {
      active = false;
      globalThis.clearInterval(intervalId);
    };
  }, [alertId]);

  async function performLifecycleAction(action: "resolve" | "cancel") {
    if (!live || pendingAction) return;
    setPendingAction(action);
    try {
      const result = action === "resolve"
        ? await resolveAlert(live.alertId, live.confirmedVersion, createIdempotencyKey())
        : await cancelAlert(live.alertId, live.confirmedVersion, createIdempotencyKey());
      setLive((current) => current ? {
        ...current,
        alertState: result.state,
        canResolve: false,
        canCancel: false,
      } : current);
      setStatus(action === "resolve" ? "Alert resolved by the operator." : "Alert cancelled by the operator.");
    } catch (error: unknown) {
      setStatus(isAlertApiError(error) && error.status === 409
        ? "The alert state changed. Refresh the live status before trying again."
        : "The operator action could not be completed.");
    } finally {
      setPendingAction(null);
    }
  }

  return (
    <SimulationChrome
      title="Simulation alert live status"
      lead="Operator view of recipient-level delivery and response states for the authenticated organization."
    >
      <p className="review-notice" role="note">
        This page polls for refreshed status; it is not guaranteed real-time monitoring.
      </p>
      <p className="status-message" role="status" aria-live="polite">{status}</p>
      {live ? (
        <>
          <section className="alert-panel" aria-labelledby="live-alert-state">
            <h2 id="live-alert-state">Alert version {live.confirmedVersion}</h2>
            <div className="review-grid">
              <div className="review-item"><strong>Alert state</strong><span>{live.alertState}</span></div>
              <div className="review-item"><strong>Dispatch outbox</strong><span>{live.outboxState}</span></div>
            </div>
            {live.manualFallbackRequired ? (
              <p className="review-notice" role="alert">
                Manual fallback is required for this simulation failure. <code>REQUIRES_HOSPITAL_DECISION</code>: no production fallback route is configured.
              </p>
            ) : null}
            {(live.canResolve || live.canCancel) ? (
              <div className="form-actions" aria-label="Operator alert lifecycle actions">
                {live.canResolve ? (
                  <button type="button" disabled={pendingAction !== null} onClick={() => void performLifecycleAction("resolve")}>
                    {pendingAction === "resolve" ? "Resolving…" : "Resolve alert"}
                  </button>
                ) : null}
                {live.canCancel ? (
                  <button type="button" disabled={pendingAction !== null} onClick={() => void performLifecycleAction("cancel")}>
                    {pendingAction === "cancel" ? "Cancelling…" : "Cancel alert"}
                  </button>
                ) : null}
              </div>
            ) : null}
          </section>
          <div className="recipient-list" aria-label="Live recipient statuses">
            {live.recipients.map((recipient) => (
              <article className="recipient-row response-card" key={recipient.practitionerId}>
                <div>
                  <h2>{recipient.displayName}</h2>
                  <p>{recipient.simulationCode} · {recipient.specialty} · On-call: {recipient.onCallSnapshot ?? "not recorded"}</p>
                  <div className="response-state-list">
                    <span>Acknowledgement: {recipient.acknowledgedAtUtc ? `recorded at ${safeUtc(recipient.acknowledgedAtUtc)}` : "not recorded"}</span>
                    <span>Terminal disposition: {recipient.terminalDisposition ?? "not recorded"}</span>
                    <span>Responsibility: {recipient.responsibilityAcceptedAtUtc ? `accepted at ${safeUtc(recipient.responsibilityAcceptedAtUtc)}` : "not accepted"}</span>
                    <span>Call unit: {recipient.callUnitRequestedAtUtc ? `requested at ${safeUtc(recipient.callUnitRequestedAtUtc)}` : "not requested"}</span>
                    <span>Last response reason: {recipient.lastResponseReasonCode ?? "not recorded"}</span>
                  </div>
                </div>
                <div className="attempt-list" aria-label={`Channel attempts for ${recipient.displayName}`}>
                  {recipient.attempts.map((attempt) => (
                    <section className="review-item" key={`${attempt.channel}-${attempt.attemptNumber}`}>
                      <strong>{attempt.channel} attempt {attempt.attemptNumber}</strong>
                      <span>Delivery: {attempt.status}</span>
                      <span>Opened: {attempt.openedAtUtc ? `occurred at ${safeUtc(attempt.openedAtUtc)}` : attempt.openedState === "NotApplicable" ? "not applicable" : "pending, not observed"}</span>
                      {attempt.failureCategory ? <span>Safe failure: {attempt.failureCategory}</span> : null}
                    </section>
                  ))}
                </div>
              </article>
            ))}
          </div>
        </>
      ) : null}
      <Link className="focus-link" href={`/alerts/${alertId}/review`}>Return to exact alert review</Link>
    </SimulationChrome>
  );
}
