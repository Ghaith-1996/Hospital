"use client";

import React from "react";
import Link from "next/link";
import { PageHeader } from "../../components/ui/page-header";
import { ScreenState } from "../../components/ui/screen-state";
import { StatusBadge } from "../../components/ui/status-badge";
import { Tabs } from "../../components/ui/tabs";
import { formatAlertDisplayTitle, selectCurrentUser, selectDoctorAlerts } from "../../features/alerts/selectors";
import { usePrototype } from "../../features/alerts/prototype-store";
import type { AlertRecord, DoctorInboxTab } from "../../features/alerts/types";

function formatDate(value: string | undefined) {
  if (!value) return "Not received";
  return new Intl.DateTimeFormat("en-CA", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

function alertTitle(alert: AlertRecord) {
  return formatAlertDisplayTitle(alert);
}

function DoctorInboxTable({ alerts }: { alerts: AlertRecord[] }) {
  return (
    <div className="table-wrap doctor-inbox__table-wrap">
      <table className="alerts-table doctor-inbox__table" aria-label="Fictional doctor inbox">
        <caption className="sr-only">Fictional doctor inbox</caption>
        <thead>
          <tr>
            <th scope="col">Alert</th>
            <th scope="col">Patient Reference</th>
            <th scope="col">Urgency</th>
            <th scope="col">Status</th>
            <th scope="col">Received</th>
          </tr>
        </thead>
        <tbody>
          {alerts.map((alert) => {
            const title = alertTitle(alert);
            return (
              <tr key={alert.id}>
                <th scope="row">
                  <span className="doctor-inbox__alert-cell">
                    <span>{title}</span>
                    <Link className="doctor-inbox__alert-link" href={`/my-alerts/${alert.id}`} aria-label={`Open ${title}`}>
                      Open
                    </Link>
                  </span>
                </th>
                <td>{alert.patientReference}</td>
                <td>
                  <StatusBadge urgency={alert.urgency} />
                </td>
                <td>
                  <StatusBadge status={alert.status} />
                </td>
                <td>{formatDate(alert.receivedAt ?? alert.updatedAt)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function DoctorInboxCards({ alerts }: { alerts: AlertRecord[] }) {
  return (
    <div className="doctor-inbox__cards" aria-label="Fictional doctor inbox cards">
      {alerts.map((alert) => {
        const title = alertTitle(alert);

        return (
          <article className="doctor-inbox-card" key={alert.id} aria-label={`${title} alert card`}>
            <div className="doctor-inbox-card__header">
              <h2>{title}</h2>
              <Link className="focus-link doctor-inbox-card__open" href={`/my-alerts/${alert.id}`}>
                Open {title}
              </Link>
            </div>
            <dl className="doctor-inbox-card__details">
              <div>
                <dt>Alert</dt>
                <dd>{title}</dd>
              </div>
              <div>
                <dt>Patient Reference</dt>
                <dd>{alert.patientReference}</dd>
              </div>
              <div>
                <dt>Urgency</dt>
                <dd>
                  <StatusBadge urgency={alert.urgency} />
                </dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd>
                  <StatusBadge status={alert.status} />
                </dd>
              </div>
              <div>
                <dt>Received</dt>
                <dd>{formatDate(alert.receivedAt ?? alert.updatedAt)}</dd>
              </div>
            </dl>
          </article>
        );
      })}
    </div>
  );
}

export default function DoctorInboxPage() {
  const { state, selectUser } = usePrototype();
  const [activeTab, setActiveTab] = React.useState<DoctorInboxTab>("all");
  const currentUser = selectCurrentUser(state);
  const marcUser = state.users.find((user) => user.clinicianId === "clinician-marc");

  if (currentUser.role !== "doctor" || !currentUser.clinicianId) {
    return (
      <ScreenState
        kind="empty"
        label="Doctor inbox requires a fictional doctor."
        description="Switch users to view alerts assigned to Dr. Marc Tremblay."
        action={
          <button
            type="button"
            className="button-primary"
            onClick={() => {
              if (marcUser) selectUser(marcUser.id);
            }}
          >
            Switch to Dr. Marc
          </button>
        }
      />
    );
  }

  const assignedAlerts = selectDoctorAlerts(state, currentUser.clinicianId, "all");
  const counts = {
    all: assignedAlerts.length,
    unread: selectDoctorAlerts(state, currentUser.clinicianId, "unread").length,
    "in-progress": selectDoctorAlerts(state, currentUser.clinicianId, "in-progress").length,
    completed: selectDoctorAlerts(state, currentUser.clinicianId, "completed").length,
  };
  const visibleAlerts = selectDoctorAlerts(state, currentUser.clinicianId, activeTab);
  const tabs = [
    { value: "all", label: "All", count: counts.all },
    { value: "unread", label: "Unread", count: counts.unread },
    { value: "in-progress", label: "In Progress", count: counts["in-progress"] },
    { value: "completed", label: "Completed", count: counts.completed },
  ] as const;

  return (
    <section className="doctor-inbox">
      <PageHeader title="Inbox" description="Alerts assigned to me." />

      <Tabs ariaLabel="Doctor inbox tabs" tabs={[...tabs]} value={activeTab} onChange={setActiveTab} />

      {visibleAlerts.length > 0 ? (
        <div className="doctor-inbox__list">
          <DoctorInboxTable alerts={visibleAlerts} />
          <DoctorInboxCards alerts={visibleAlerts} />
        </div>
      ) : (
        <ScreenState
          kind="empty"
          label={assignedAlerts.length === 0 ? "No alerts assigned to me." : `No ${tabs.find((tab) => tab.value === activeTab)?.label.toLowerCase()} alerts.`}
          description={
            assignedAlerts.length === 0
              ? "This fictional inbox will list alerts assigned to the selected doctor."
              : "Other fictional alerts exist for this doctor, but none are in the selected inbox tab."
          }
          headingLevel="h2"
        />
      )}
    </section>
  );
}
