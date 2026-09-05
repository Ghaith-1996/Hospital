import React from "react";
import Link from "next/link";
import { StatusBadge } from "../ui/status-badge";
import type { AlertRecord } from "../../features/alerts/types";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-CA", {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(new Date(value));
}

function respondedCount(alert: AlertRecord) {
  return alert.recipients.filter((recipient) => recipient.response !== "none").length;
}

export function AlertList({
  alerts,
}: {
  alerts: AlertRecord[];
}) {
  return (
    <div className="alert-list">
      <div className="table-wrap alert-table-wrap">
        <table className="alerts-table" aria-label="Fictional alerts">
          <caption className="sr-only">Fictional alerts</caption>
          <thead>
            <tr>
              <th scope="col">Patient Reference</th>
              <th scope="col">Urgency</th>
              <th scope="col">Status</th>
              <th scope="col">Recipients</th>
              <th scope="col">Last Updated</th>
              <th scope="col">
                <span className="sr-only">Open alert</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {alerts.map((alert) => (
              <tr key={alert.id}>
                <th scope="row">{alert.patientReference}</th>
                <td>
                  <StatusBadge urgency={alert.urgency} />
                </td>
                <td>
                  <StatusBadge status={alert.status === "escalating" ? "in-progress" : alert.status} label={alert.status === "escalating" ? "Escalating" : undefined} tone={alert.status === "escalating" ? "critical" : undefined} />
                </td>
                <td>
                  {respondedCount(alert)}/{alert.recipients.length}
                </td>
                <td>{formatDate(alert.updatedAt)}</td>
                <td>
                  <Link className="focus-link alert-open-link" href={`/alerts/${alert.id}`}>
                    Open {alert.patientReference}
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="alert-cards" aria-label="Fictional alert cards">
        {alerts.map((alert) => (
          <article className="alert-card" key={alert.id}>
            <div className="alert-card__header">
              <h2>{alert.patientReference}</h2>
              <Link className="focus-link alert-open-link" href={`/alerts/${alert.id}`}>
                Open {alert.patientReference}
              </Link>
            </div>
            <dl className="alert-card__details">
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
                  <StatusBadge status={alert.status === "escalating" ? "in-progress" : alert.status} label={alert.status === "escalating" ? "Escalating" : undefined} tone={alert.status === "escalating" ? "critical" : undefined} />
                </dd>
              </div>
              <div>
                <dt>Recipients</dt>
                <dd>
                  {respondedCount(alert)}/{alert.recipients.length}
                </dd>
              </div>
              <div>
                <dt>Last Updated</dt>
                <dd>{formatDate(alert.updatedAt)}</dd>
              </div>
            </dl>
          </article>
        ))}
      </div>
    </div>
  );
}
