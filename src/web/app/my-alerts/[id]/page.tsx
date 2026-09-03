"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { SimulationChrome } from "../../simulation-chrome";
import {
  createIdempotencyKey,
  getMyAlert,
  isAlertApiError,
  markMyAlertOpened,
  MyAlertDetail,
  recordMyAlertResponse,
} from "../../../lib/alerts";

type ResponseType = "Acknowledged" | "Accepted" | "Declined" | "Unavailable";

function routeAlertId(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function safeUtc(value: string | null): string {
  if (!value) return "not recorded";
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? "not recorded" : parsed.toISOString();
}

function openedLabel(alert: MyAlertDetail): string {
  if (alert.openedState === "NotApplicable") return "not applicable";
  if (alert.openedState === "Failed") return "failed";
  if (alert.openedState === "Occurred") {
    return alert.secureMessageOpenedAtUtc
      ? `occurred at ${safeUtc(alert.secureMessageOpenedAtUtc)}`
      : "occurred";
  }
  return "pending, not observed";
}

function detailError(error: unknown): string {
  if (isAlertApiError(error) && error.status === 401) return "Sign in with the seeded Practitioner identity.";
  if (isAlertApiError(error) && error.status === 403) return "This identity does not have a linked practitioner inbox.";
  if (isAlertApiError(error) && error.status === 404) return "This active alert is not addressed to the authenticated practitioner.";
  return "The addressed simulation alert could not be loaded. Reload and try again.";
}

export default function MyAlertDetailPage() {
  const params = useParams<{ id: string | string[] }>();
  const alertId = routeAlertId(params.id);
  const [alert, setAlert] = useState<MyAlertDetail | null>(null);
  const [loadedAlertId, setLoadedAlertId] = useState<string | null>(null);
  const [status, setStatus] = useState("Loading the addressed simulation alert.");
  const [submittingAction, setSubmittingAction] = useState<ResponseType | null>(null);
  const submittingRef = useRef(false);

  useEffect(() => {
    let active = true;
    void getMyAlert(alertId)
      .then(async (loaded) => {
        if (!active) return;
        setAlert(loaded);
        setLoadedAlertId(alertId);
        setStatus("Review the approved message and exact confirmed fields before recording a response.");
        if (loaded.channels.includes("SecureMessage") && !loaded.secureMessageOpenedAtUtc) {
          try {
            const opened = await markMyAlertOpened(
              alertId,
              loaded.confirmedVersion,
              createIdempotencyKey(),
            );
            if (active) {
              setAlert((current) => current ? {
                ...current,
                openedState: opened.secureMessageOpenedAtUtc ? "Occurred" : current.openedState,
                secureMessageOpenedAtUtc: opened.secureMessageOpenedAtUtc,
              } : current);
              setStatus(
                opened.secureMessageOpenedAtUtc
                  ? `SecureMessage opened at ${safeUtc(opened.secureMessageOpenedAtUtc)}.`
                  : "This alert has no SecureMessage open observation.",
              );
            }
          } catch {
            if (active) setStatus("The alert loaded, but its SecureMessage open observation could not be recorded.");
          }
        }
      })
      .catch((error: unknown) => {
        if (active) {
          setLoadedAlertId(alertId);
          setStatus(detailError(error));
        }
      });
    return () => {
      active = false;
    };
  }, [alertId]);

  async function respond(responseType: ResponseType) {
    if (!alert || submittingRef.current) return;
    if (responseType === "Acknowledged" && alert.acknowledgedAtUtc) return;
    if (responseType !== "Acknowledged" && alert.terminalDisposition) return;

    submittingRef.current = true;
    setSubmittingAction(responseType);
    try {
      const result = await recordMyAlertResponse(
        alertId,
        alert.confirmedVersion,
        responseType,
        createIdempotencyKey(),
      );
      setAlert((current) => current ? {
        ...current,
        acknowledgedAtUtc: result.acknowledgedAtUtc,
        terminalDisposition: result.terminalDisposition,
        responsibilityAcceptedAtUtc: result.responsibilityAcceptedAtUtc,
      } : current);
      setStatus(
        responseType === "Accepted"
          ? "Responsibility accepted. The alert remains active."
          : responseType === "Acknowledged"
            ? "Acknowledgement recorded. Responsibility has not been accepted."
            : `${responseType} recorded. Phase 8 does not trigger escalation.`,
      );
    } catch (error: unknown) {
      setStatus(
        isAlertApiError(error) && error.status === 409
          ? "The alert or response state changed. Reload before trying another response."
          : "The response could not be recorded. Review the alert and retry.",
      );
    } finally {
      submittingRef.current = false;
      setSubmittingAction(null);
    }
  }

  if (loadedAlertId !== alertId) {
    return (
      <SimulationChrome title="Addressed simulation alert" lead="Loading the server-scoped practitioner view.">
        <p className="status-message" role="status" aria-live="polite">{status}</p>
      </SimulationChrome>
    );
  }

  if (!alert) {
    return (
      <SimulationChrome title="Addressed simulation alert" lead="The practitioner detail is unavailable.">
        <p className="status-message" role="status" aria-live="polite">{status}</p>
        <Link className="focus-link" href="/my-alerts">Return to my alerts</Link>
      </SimulationChrome>
    );
  }

  const terminalRecorded = Boolean(alert.terminalDisposition);
  const responsePending = submittingAction !== null;
  return (
    <SimulationChrome
      title="Addressed simulation alert"
      lead={`Version ${alert.confirmedVersion} · ${alert.location} · ${alert.urgencyLabel}`}
    >
      <section className="alert-panel" aria-labelledby="recipient-approved-message">
        <h2 id="recipient-approved-message">Approved message</h2>
        <p className="protected-copy">{alert.approvedMessage}</p>
        <p>Synthetic patient reference: {alert.simulationPatientReference}</p>
      </section>

      <section className="alert-panel" aria-labelledby="recipient-critical-fields">
        <h2 id="recipient-critical-fields">Confirmed critical values</h2>
        <div className="review-grid">
          {alert.criticalFields.map((field) => (
            <div className="review-item" key={field.fieldId}>
              <strong>{field.fieldId}</strong>
              <span>{field.normalizedValue} {field.unit ?? ""}</span>
            </div>
          ))}
        </div>
      </section>

      <section className="confirmation-panel" aria-labelledby="recipient-response-actions">
        <h2 id="recipient-response-actions">Simulation response actions</h2>
        <p>Acknowledgement does not accept responsibility.</p>
        <p>Acceptance records responsibility but does not resolve this alert.</p>
        <div className="form-actions">
          <button
            type="button"
            disabled={responsePending || Boolean(alert.acknowledgedAtUtc)}
            onClick={() => void respond("Acknowledged")}
          >
            {submittingAction === "Acknowledged" ? "Recording acknowledgement…" : "Acknowledge"}
          </button>
          <button type="button" disabled={responsePending || terminalRecorded} onClick={() => void respond("Accepted")}>
            {submittingAction === "Accepted" ? "Accepting responsibility…" : "Accept responsibility"}
          </button>
          <button type="button" disabled={responsePending || terminalRecorded} onClick={() => void respond("Declined")}>
            Decline
          </button>
          <button type="button" disabled={responsePending || terminalRecorded} onClick={() => void respond("Unavailable")}>
            Mark unavailable
          </button>
        </div>
        <div className="response-state-list" aria-label="Current practitioner response states">
          <span>Channels: {alert.channels.join(", ")}</span>
          <span>Opened: {openedLabel(alert)}</span>
          <span>Acknowledgement: {alert.acknowledgedAtUtc ? `recorded at ${safeUtc(alert.acknowledgedAtUtc)}` : "not recorded"}</span>
          <span>Terminal disposition: {alert.terminalDisposition ?? "not recorded"}</span>
          <span>Responsibility: {alert.responsibilityAcceptedAtUtc ? `accepted at ${safeUtc(alert.responsibilityAcceptedAtUtc)}` : "not accepted"}</span>
        </div>
      </section>

      <p className="status-message" role="status" aria-live="polite">{status}</p>
      <Link className="focus-link" href="/my-alerts">Return to my alerts</Link>
    </SimulationChrome>
  );
}
