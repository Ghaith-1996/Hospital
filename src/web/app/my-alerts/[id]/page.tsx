"use client";

import React from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { ResponsePanel, responseLabel } from "../../../components/alerts/response-panel";
import { PageHeader } from "../../../components/ui/page-header";
import { ScreenState } from "../../../components/ui/screen-state";
import { StatusBadge } from "../../../components/ui/status-badge";
import { selectAlertById, selectCurrentUser } from "../../../features/alerts/selectors";
import { usePrototype } from "../../../features/alerts/prototype-store";
import type { AlertRecord, Clinician, DoctorResponse } from "../../../features/alerts/types";

const alertTitles: Record<string, string> = {
  "alert-critical-1": "Chest pain, hypotension",
  "alert-in-progress-1": "Respiratory distress",
  "alert-escalating-1": "Suspected sepsis",
};

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
  }).format(new Date(value));
}

function fallbackTitle(alert: AlertRecord) {
  return alert.label
    .replace(/^SIMULATION:\s*/i, "")
    .replace(/^fictional\s*/i, "")
    .replace(/[.!?]\s*$/u, "");
}

function alertTitle(alert: AlertRecord) {
  return alertTitles[alert.id] ?? fallbackTitle(alert);
}

function findClinician(clinicians: Clinician[], clinicianId: string) {
  return clinicians.find((clinician) => clinician.id === clinicianId);
}

function currentResponseMessage(response: DoctorResponse) {
  if (response === "accepted") return "Responsibility accepted in this local simulation.";
  if (response === "acknowledged") return "Acknowledgement recorded without accepting responsibility.";
  if (response === "declined") return "Decline recorded for this fictional case.";
  if (response === "unavailable") return "Unavailable response recorded for this fictional case.";
  return "Choose a response without sending anything outside this local prototype.";
}

function DetailItem({ label, children }: React.PropsWithChildren<{ label: string }>) {
  return (
    <div className="doctor-alert__item">
      <dt>{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

export default function DoctorAlertPage() {
  const params = useParams<{ id?: string | string[] }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const alertId = readRouteId(params.id);
  const { state } = usePrototype();
  const currentUser = selectCurrentUser(state);
  const alert = alertId ? selectAlertById(state, alertId) : undefined;

  if (!alertId || !alert || currentUser.role !== "doctor" || !currentUser.clinicianId) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This local prototype could not find that fictional doctor alert."
        action={
          <Link className="focus-link" href="/my-alerts">
            Back to Inbox
          </Link>
        }
      />
    );
  }

  const clinicianId = currentUser.clinicianId;
  const currentRecipient = alert.recipients.find((recipient) => recipient.clinicianId === clinicianId);

  if (!currentRecipient) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This fictional alert is not assigned to the selected doctor."
        action={
          <Link className="focus-link" href="/my-alerts">
            Back to Inbox
          </Link>
        }
      />
    );
  }

  const otherRecipients = alert.recipients.filter((recipient) => recipient.clinicianId !== clinicianId);
  const responded = searchParams.get("responded") === "1";

  return (
    <section className="doctor-alert">
      <Link className="focus-link" href="/my-alerts">
        Back to Inbox
      </Link>
      {responded ? (
        <div className="doctor-alert__success" role="status" aria-label="Fictional response saved">
          Fictional response saved. Your current response is {responseLabel(currentRecipient.response)}.
        </div>
      ) : null}
      <PageHeader
        title={alertTitle(alert)}
        description={`Received: ${formatDateTime(alert.receivedAt ?? alert.updatedAt)}`}
        actions={<StatusBadge urgency={alert.urgency} />}
      />

      <div className="doctor-alert__grid">
        <section className="doctor-alert__card" aria-labelledby="doctor-alert-patient-heading">
          <h2 id="doctor-alert-patient-heading">Patient Reference</h2>
          <dl className="doctor-alert__facts">
            <DetailItem label="Patient Reference">{alert.patientReference}</DetailItem>
            <DetailItem label="Location">{alert.location}</DetailItem>
            <DetailItem label="Department">{alert.department}</DetailItem>
          </dl>
        </section>

        <section className="doctor-alert__card doctor-alert__card--case" aria-labelledby="doctor-alert-case-heading">
          <h2 id="doctor-alert-case-heading">Case Details</h2>
          <p>{alert.caseDetails}</p>
        </section>

        <section className="doctor-alert__card" role="region" aria-label="Other Recipients">
          <h2>Other Recipients</h2>
          <ul className="doctor-alert__recipients">
            {otherRecipients.map((recipient) => {
              const clinician = findClinician(state.clinicians, recipient.clinicianId);
              return (
                <li key={recipient.clinicianId}>
                  <span className="clinician-avatar" aria-hidden="true">
                    {clinician?.initials ?? "FC"}
                  </span>
                  <span>
                    <strong>{clinician?.displayName ?? "Fictional clinician"}</strong>
                    <span>{clinician?.specialty ?? "Simulation clinician"}</span>
                  </span>
                </li>
              );
            })}
          </ul>
        </section>
      </div>

      <div className="doctor-alert__current">
        <p>Your current response: {responseLabel(currentRecipient.response)}</p>
        <p>{currentResponseMessage(currentRecipient.response)}</p>
      </div>

      <ResponsePanel
        alertId={alert.id}
        currentResponse={currentRecipient.response}
        onChoose={(response) => router.push(`/my-alerts/${alert.id}/respond?response=${response}`)}
      />
    </section>
  );
}
