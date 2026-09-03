"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { SimulationChrome } from "../simulation-chrome";
import { getMyAlerts, isAlertApiError, MyAlertSummary } from "../../lib/alerts";

function inboxError(error: unknown): string {
  if (isAlertApiError(error) && error.status === 401) {
    return "Sign in with the seeded Practitioner identity to open the simulation inbox.";
  }
  if (isAlertApiError(error) && error.status === 403) {
    return "This identity does not have a practitioner inbox.";
  }
  return "The practitioner inbox could not be loaded. Reload and try again.";
}

function stateLabel(value: string): string {
  return value === "PendingNotObserved"
    ? "pending, not observed"
    : value === "NotApplicable"
      ? "not applicable"
      : value.toLowerCase();
}

export default function MyAlertsPage() {
  const [alerts, setAlerts] = useState<MyAlertSummary[] | null>(null);
  const [status, setStatus] = useState("Loading alerts addressed to the authenticated practitioner.");

  useEffect(() => {
    let active = true;
    void getMyAlerts()
      .then((result) => {
        if (active) {
          setAlerts(result);
          setStatus(result.length === 0 ? "No active simulation alerts are addressed to this practitioner." : "Inbox refreshed.");
        }
      })
      .catch((error: unknown) => {
        if (active) {
          setAlerts([]);
          setStatus(inboxError(error));
        }
      });
    return () => {
      active = false;
    };
  }, []);

  return (
    <SimulationChrome
      title="My simulation alerts"
      lead="This server-scoped inbox contains only active alerts addressed to the practitioner linked to the authenticated simulation user."
    >
      <p className="review-notice" role="note">
        Opened, acknowledged, terminal disposition, and responsibility are separate states. Phase 8 does not escalate or resolve alerts.
      </p>
      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
      {alerts ? (
        <div className="recipient-list" aria-label="Addressed simulation alerts">
          {alerts.map((alert) => (
            <article className="recipient-row response-card" key={`${alert.alertId}-${alert.confirmedVersion}`}>
              <div>
                <h2>{alert.location}</h2>
                <p>
                  {alert.urgencyLabel} · version {alert.confirmedVersion} · {alert.state}
                </p>
                <p>Channels: {alert.channels.join(", ")}</p>
              </div>
              <div className="response-state-list" aria-label={`Response states for ${alert.location}`}>
                <span>Opened: {stateLabel(alert.openedState)}</span>
                <span>Acknowledgement: {alert.acknowledgedAtUtc ? "recorded" : "not recorded"}</span>
                <span>Terminal disposition: {alert.terminalDisposition ?? "not recorded"}</span>
                <span>Responsibility: {alert.responsibilityAcceptedAtUtc ? "accepted" : "not accepted"}</span>
                <Link className="focus-link" href={`/my-alerts/${alert.alertId}`}>
                  Open addressed alert
                </Link>
              </div>
            </article>
          ))}
        </div>
      ) : null}
    </SimulationChrome>
  );
}
