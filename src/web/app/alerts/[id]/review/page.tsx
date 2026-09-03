"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { SimulationChrome } from "../../../simulation-chrome";
import {
  AlertReview,
  ConfirmResult,
  createIdempotencyKey,
  confirmAlertReview,
  getAlertReview,
  isAlertApiError,
} from "../../../../lib/alerts";

function routeAlertId(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function safeUtc(value: string | null): string {
  if (!value) {
    return "not available";
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? "not available" : parsed.toISOString();
}

function reviewErrorStatus(error: unknown): string {
  if (isAlertApiError(error) && error.status === 401) {
    return "Sign in with a seeded Operator or Administrator identity to review this simulation alert.";
  }
  if (isAlertApiError(error) && error.status === 403) {
    return "This identity cannot confirm simulation alerts.";
  }
  if (isAlertApiError(error) && error.status === 409) {
    return "This alert changed before confirmation. Return to compose and open the current exact review.";
  }
  return "The exact simulation review could not be loaded. Reload and try again.";
}

export default function AlertReviewPage() {
  const params = useParams<{ id: string | string[] }>();
  const alertId = routeAlertId(params.id);
  const [review, setReview] = useState<AlertReview | null>(null);
  const [loadedAlertId, setLoadedAlertId] = useState<string | null>(null);
  const [result, setResult] = useState<ConfirmResult | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [status, setStatus] = useState("Loading the exact protected alert review.");
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const [idempotencyKey] = useState(() => createIdempotencyKey());

  useEffect(() => {
    let active = true;
    void getAlertReview(alertId)
      .then((loadedReview) => {
        if (active) {
          setLoadedAlertId(alertId);
          setReview(loadedReview);
          setResult(null);
          setAcknowledged(false);
          setStatus("Review the exact version, values, units, recipients, channels, approved message, and policies.");
        }
      })
      .catch((error: unknown) => {
        if (active) {
          setLoadedAlertId(alertId);
          setStatus(reviewErrorStatus(error));
        }
      });

    return () => {
      active = false;
    };
  }, [alertId]);

  async function confirmReview() {
    if (!review || acknowledged === false || submittingRef.current || result) {
      return;
    }
    submittingRef.current = true;
    setSubmitting(true);
    try {
      const confirmed = await confirmAlertReview(alertId, review.draftVersion, idempotencyKey);
      setResult(confirmed);
      setStatus(
        confirmed.replayed
          ? "This exact confirmation was already recorded. The simulation alert remains queued for simulation dispatch."
          : "Simulation alert queued for simulation dispatch.",
      );
    } catch (error) {
      setStatus(
        isAlertApiError(error) && error.status === 409
          ? "This alert changed before confirmation. Return to compose and open the current exact review."
          : "The exact confirmation could not be recorded. Review the displayed version and retry.",
      );
      submittingRef.current = false;
    } finally {
      setSubmitting(false);
    }
  }

  if (loadedAlertId !== alertId) {
    return (
      <SimulationChrome
        title="Exact alert review"
        lead="Load the protected exact version for deliberate human confirmation."
      >
        <p className="status-message" role="status" aria-live="polite">
          {status}
        </p>
      </SimulationChrome>
    );
  }

  if (!review) {
    return (
      <SimulationChrome
        title="Exact alert review"
        lead="The exact simulation review is not available for this alert version."
      >
        <p className="status-message" role="status" aria-live="polite">
          {status}
        </p>
        <Link className="focus-link" href={`/alerts/${alertId}/compose`}>
          Return to compose
        </Link>
      </SimulationChrome>
    );
  }

  return (
    <SimulationChrome
      title="Exact alert review"
      lead="Confirm only after checking the exact version, approved message, critical values and units, complete recipient set, channels, and displayed policy versions."
    >
      <div className="review-notice" role="note">
        Confirmation queues the Development/Test-only simulation worker. It does not contact a real provider, escalate, or resolve the alert.
      </div>

      <section className="alert-panel" aria-labelledby="review-version-heading">
        <h2 id="review-version-heading">Draft version {review.draftVersion}</h2>
        <div className="review-grid">
          <div className="review-item">
            <strong>Synthetic patient reference</strong>
            <span>{review.simulationPatientReference}</span>
          </div>
          <div className="review-item">
            <strong>Simulation location</strong>
            <span>{review.location}</span>
          </div>
          <div className="review-item">
            <strong>Urgency label</strong>
            <span>{review.urgencyLabel}</span>
          </div>
          <div className="review-item">
            <strong>State</strong>
            <span>{review.state}</span>
          </div>
        </div>
      </section>

      <section className="alert-panel" aria-labelledby="review-message-heading">
        <h2 id="review-message-heading">Approved message</h2>
        <p className="protected-copy">{review.approvedMessage}</p>
      </section>

      <section className="alert-panel" aria-labelledby="review-critical-heading">
        <h2 id="review-critical-heading">Confirmed critical values and units</h2>
        <div className="review-grid">
          {review.criticalFields.map((field) => (
            <div className="review-item" key={`${field.alertVersion}-${field.fieldId}`}>
              <strong>{field.fieldId}</strong>
              <span>
                {field.normalizedValue} {field.unit ?? ""}
              </span>
              <span>Confirmed by authenticated simulation user</span>
            </div>
          ))}
        </div>
      </section>

      <section className="alert-panel" aria-labelledby="review-recipient-heading">
        <h2 id="review-recipient-heading">Complete recipient set and channels</h2>
        {review.recipients.length === 0 ? <p>No recipients are present; confirmation is unavailable.</p> : null}
        <div className="recipient-list">
          {review.recipients.map((recipient) => (
            <article className="recipient-row" key={`${recipient.practitionerId}-${recipient.channel}`}>
              <div>
                <h3>{recipient.displayName}</h3>
                <p>
                  {recipient.roleTitle ?? recipient.specialty} · {recipient.department ?? "Department not listed"} · {recipient.site ?? "Site not listed"}
                </p>
              </div>
              <div className="channel-evidence">
                <strong>Channel: {recipient.channel}</strong>
                <span>
                  On-call snapshot: <span>{recipient.onCallSnapshot ?? "Not available"}</span>
                </span>
                <span>Directory freshness: {recipient.isStale ? "stale" : "fresh"}</span>
                <span>Directory source updated: {safeUtc(recipient.directorySourceUpdatedAtUtc)}</span>
                <span>Directory revision: {recipient.directoryRevision}</span>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="alert-panel" aria-labelledby="review-policy-heading">
        <h2 id="review-policy-heading">Policy versions shown for confirmation</h2>
        <div className="review-grid">
          <div className="review-item">
            <strong>Escalation policy</strong>
            <span>{review.demoEscalationPolicyVersion}</span>
          </div>
          <div className="review-item">
            <strong>Notification policy</strong>
            <span>{review.demoNotificationPolicyVersion}</span>
          </div>
        </div>
      </section>

      <section className="confirmation-panel" aria-labelledby="review-confirm-heading">
        <h2 id="review-confirm-heading">Human confirmation</h2>
        <label className="confirmation-check">
          <input
            type="checkbox"
            checked={acknowledged}
            onChange={(event) => setAcknowledged(event.target.checked)}
            disabled={submitting || Boolean(result)}
          />
          I reviewed the exact alert version, critical values and units, recipients, channels, approved message, and policy versions.
        </label>
        <div className="form-actions">
          <button
            type="button"
            onClick={() => void confirmReview()}
            disabled={!acknowledged || submitting || Boolean(result) || review.recipients.length === 0}
          >
            {submitting ? "Recording confirmation…" : result ? "Confirmation recorded" : "Confirm and queue simulation alert"}
          </button>
          <Link className="button-secondary" href={`/alerts/${alertId}/compose`}>
            Return to compose
          </Link>
          {result ? (
            <Link className="button-secondary" href={`/alerts/${alertId}/live`}>
              Open refreshed live status
            </Link>
          ) : null}
        </div>
      </section>

      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
    </SimulationChrome>
  );
}
