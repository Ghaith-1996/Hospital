"use client";

import React from "react";
import { useParams, useRouter } from "next/navigation";
import { ProgressSteps } from "../../../../components/alerts/progress-steps";
import { ConfirmDialog } from "../../../../components/ui/confirm-dialog";
import { ScreenState } from "../../../../components/ui/screen-state";
import { StatusBadge } from "../../../../components/ui/status-badge";
import { selectAlertById } from "../../../../features/alerts/selectors";
import { usePrototype } from "../../../../features/alerts/prototype-store";
import type { Clinician } from "../../../../features/alerts/types";

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function selectedClinicians(clinicians: Clinician[], clinicianIds: string[]) {
  return clinicianIds
    .map((id) => clinicians.find((clinician) => clinician.id === id))
    .filter((clinician): clinician is Clinician => Boolean(clinician));
}

export default function AlertReviewPage() {
  const router = useRouter();
  const params = useParams<{ id?: string | string[] }>();
  const alertId = readRouteId(params.id);
  const { confirmAlert, state } = usePrototype();
  const [dialogOpen, setDialogOpen] = React.useState(false);
  const alert = alertId ? selectAlertById(state, alertId) : undefined;

  if (!alertId || !alert) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This local prototype could not find that fictional alert."
        action={
          <a className="focus-link" href="/alerts/new">
            Create another alert
          </a>
        }
      />
    );
  }

  const reviewedAlert = alert;
  const clinicians = selectedClinicians(
    state.clinicians,
    reviewedAlert.recipients.map((recipient) => recipient.clinicianId),
  );
  const recipientNames = clinicians.map((clinician) => clinician.displayName);

  function handleConfirm() {
    confirmAlert(reviewedAlert.id);
    setDialogOpen(false);
    router.push(`/alerts/${reviewedAlert.id}/sent`);
  }

  return (
    <div className="review-page">
      <ProgressSteps current={2} />

      <header className="review-page__header">
        <div>
          <h1>Review &amp; Confirm Alert</h1>
          <p>Review all details carefully. Confirm to proceed.</p>
        </div>
      </header>

      <section className="review-card" aria-labelledby="alert-preview-heading">
        <div className="section-heading">
          <h2 id="alert-preview-heading">Alert Preview</h2>
          <a className="button-secondary" href={`/alerts/new?edit=${reviewedAlert.id}`}>
            Back/Edit
          </a>
        </div>

        <dl className="review-details">
          <div>
            <dt>Patient Reference</dt>
            <dd>{reviewedAlert.patientReference}</dd>
          </div>
          <div>
            <dt>Urgency Level</dt>
            <dd>
              <StatusBadge urgency={reviewedAlert.urgency} />
            </dd>
          </div>
          <div>
            <dt>Location</dt>
            <dd>{reviewedAlert.location}</dd>
          </div>
          <div>
            <dt>Department</dt>
            <dd>{reviewedAlert.department}</dd>
          </div>
        </dl>

        <section className="review-case" aria-labelledby="case-details-heading">
          <h3 id="case-details-heading">Case Details</h3>
          <p>{reviewedAlert.caseDetails}</p>
        </section>
      </section>

      <section className="review-card" aria-labelledby="selected-clinicians-heading">
        <h2 id="selected-clinicians-heading">Selected Clinician(s) ({clinicians.length})</h2>
        <ul className="review-clinicians">
          {clinicians.map((clinician) => (
            <li key={clinician.id}>
              <span className="clinician-avatar" aria-hidden="true">
                {clinician.initials}
              </span>
              <span>
                <strong>{clinician.displayName}</strong>
                <span>{clinician.specialty}</span>
              </span>
            </li>
          ))}
        </ul>
      </section>

      <p className="simulation-notice">
        By confirming, you acknowledge this is a fictional local prototype and that no real clinician will be contacted.
      </p>

      <div className="form-actions review-actions">
        <button type="button" className="button-secondary" onClick={() => router.push("/alerts")}>
          Cancel
        </button>
        <button type="button" onClick={() => setDialogOpen(true)}>
          Confirm &amp; Dispatch
        </button>
      </div>

      <ConfirmDialog
        open={dialogOpen}
        recipientNames={recipientNames}
        onCancel={() => setDialogOpen(false)}
        onConfirm={handleConfirm}
      />
    </div>
  );
}
