"use client";

import React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ActivityTimeline } from "../../../components/alerts/activity-timeline";
import { EscalationTimeline } from "../../../components/alerts/escalation-timeline";
import { ResponseSummary, deliveryStateLabel } from "../../../components/alerts/response-summary";
import { PageHeader } from "../../../components/ui/page-header";
import { ScreenState } from "../../../components/ui/screen-state";
import { StatusBadge } from "../../../components/ui/status-badge";
import { selectAlertById } from "../../../features/alerts/selectors";
import { usePrototype } from "../../../features/alerts/prototype-store";
import type { AlertRecord, AlertRecipient, Clinician, DoctorResponse } from "../../../features/alerts/types";

const responseLabels: Record<DoctorResponse, string> = {
  none: "No response",
  acknowledged: "Acknowledged",
  accepted: "Accepted",
  declined: "Declined",
  unavailable: "Unavailable",
};

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
  }).format(new Date(value));
}

function findClinician(clinicians: Clinician[], clinicianId: string) {
  return clinicians.find((clinician) => clinician.id === clinicianId);
}

function selectedClinicians(clinicians: Clinician[], recipients: AlertRecipient[]) {
  return recipients.map((recipient) => ({
    recipient,
    clinician: findClinician(clinicians, recipient.clinicianId),
  }));
}

function DetailMetric({ label, children }: React.PropsWithChildren<{ label: string }>) {
  return (
    <div className="detail-metric">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

function PolicyAction() {
  return (
    <div className="policy-action">
      <button type="button" className="button-secondary" aria-label="View Policy - Coming later" disabled>
        View Policy
      </button>
      <p>Coming later: fictional escalation policy review is not connected in this prototype.</p>
    </div>
  );
}

function AlertInformation({ alert }: { alert: AlertRecord }) {
  return (
    <section className="detail-card" aria-labelledby="alert-information-heading">
      <h2 id="alert-information-heading">Alert Information</h2>
      <dl className="detail-grid">
        <DetailMetric label="Patient Reference">{alert.patientReference}</DetailMetric>
        <DetailMetric label="Urgency">
          <StatusBadge urgency={alert.urgency} />
        </DetailMetric>
        <DetailMetric label="Status">
          <StatusBadge status={alert.status} />
        </DetailMetric>
        <DetailMetric label="Delivery state">{deliveryStateLabel(alert.deliveryState)}</DetailMetric>
        <DetailMetric label="Location">{alert.location}</DetailMetric>
        <DetailMetric label="Department">{alert.department}</DetailMetric>
        <DetailMetric label="Created">{formatDateTime(alert.createdAt)}</DetailMetric>
        <DetailMetric label="Last updated">{formatDateTime(alert.updatedAt)}</DetailMetric>
      </dl>
    </section>
  );
}

function CaseDetails({ alert }: { alert: AlertRecord }) {
  return (
    <section className="detail-card" aria-labelledby="case-details-heading">
      <h2 id="case-details-heading">Case Details</h2>
      <p className="case-copy">{alert.caseDetails}</p>
    </section>
  );
}

function SelectedClinicians({ alert, clinicians }: { alert: AlertRecord; clinicians: Clinician[] }) {
  const clinicianRows = selectedClinicians(clinicians, alert.recipients);

  return (
    <section className="detail-card" aria-labelledby="selected-clinicians-heading">
      <h2 id="selected-clinicians-heading">Selected Clinicians</h2>
      <ul className="detail-clinicians">
        {clinicianRows.map(({ clinician, recipient }) => (
          <li key={recipient.clinicianId}>
            <span className="clinician-avatar" aria-hidden="true">
              {clinician?.initials ?? "FC"}
            </span>
            <span>
              <strong>{clinician?.displayName ?? "Fictional clinician"}</strong>
              <span>{clinician?.specialty ?? "Simulation clinician"}</span>
            </span>
            <span className={`response-pill response-pill--${recipient.response}`}>
              {responseLabels[recipient.response]}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function StandardDetails({ alert, clinicians }: { alert: AlertRecord; clinicians: Clinician[] }) {
  return (
    <div className="alert-details-page">
      <Link className="focus-link" href="/alerts">
        Back to Alerts
      </Link>
      <PageHeader
        title="Alert Details"
        description="Monitor this fictional alert while delivery, acknowledgement, and responsibility acceptance remain separate."
        actions={<PolicyAction />}
      />

      <div className="detail-top-grid">
        <AlertInformation alert={alert} />
        <CaseDetails alert={alert} />
        <SelectedClinicians alert={alert} clinicians={clinicians} />
      </div>

      <div className="detail-lower-grid">
        <ActivityTimeline activities={alert.activities} />
        <ResponseSummary alert={alert} clinicians={clinicians} />
      </div>
    </div>
  );
}

function EscalatingDetails({ alert, clinicians }: { alert: AlertRecord; clinicians: Clinician[] }) {
  return (
    <div className="alert-details-page alert-details-page--escalating">
      <Link className="focus-link" href="/alerts">
        Back to Alerts
      </Link>
      <PageHeader
        title="Alert Escalation"
        description="Fixed demonstration state for a fictional escalation. No automatic escalation engine is running."
        actions={<PolicyAction />}
      />

      <section className="escalation-hero" aria-labelledby="escalation-status-heading">
        <div>
          <h2 id="escalation-status-heading">Escalating to fictional on-call cardiologist</h2>
          <p>DEMO elapsed time: 12 min</p>
        </div>
        <div className="escalation-hero__badges">
          <StatusBadge urgency={alert.urgency} />
          <StatusBadge status={alert.status} />
        </div>
      </section>

      <dl className="detail-metadata-row">
        <DetailMetric label="Patient Reference">{alert.patientReference}</DetailMetric>
        <DetailMetric label="Delivery state">{deliveryStateLabel(alert.deliveryState)}</DetailMetric>
        <DetailMetric label="Department">{alert.department}</DetailMetric>
        <DetailMetric label="Last updated">{formatDateTime(alert.updatedAt)}</DetailMetric>
      </dl>

      <div className="detail-top-grid">
        <AlertInformation alert={alert} />
        <CaseDetails alert={alert} />
        <SelectedClinicians alert={alert} clinicians={clinicians} />
      </div>

      <div className="detail-lower-grid">
        <EscalationTimeline steps={alert.escalationSteps} />
        <ResponseSummary alert={alert} clinicians={clinicians} />
      </div>

      <ActivityTimeline activities={alert.activities} />
    </div>
  );
}

export default function AlertDetailsPage() {
  const params = useParams<{ id?: string | string[] }>();
  const alertId = readRouteId(params.id);
  const { state } = usePrototype();
  const alert = alertId ? selectAlertById(state, alertId) : undefined;

  if (!alertId || !alert) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This local prototype could not find that fictional alert."
        action={
          <Link className="focus-link" href="/alerts">
            Back to Alerts
          </Link>
        }
      />
    );
  }

  if (alert.status === "escalating") {
    return <EscalatingDetails alert={alert} clinicians={state.clinicians} />;
  }

  return <StandardDetails alert={alert} clinicians={state.clinicians} />;
}
