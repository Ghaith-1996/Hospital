import React from "react";
import type { AlertRecord, Clinician, DeliveryState } from "../../features/alerts/types";

const deliveryLabels: Record<DeliveryState, string> = {
  "not-observed": "Not observed",
  submitted: "Submitted",
  delivered: "Delivered",
  failed: "Failed",
  "not-applicable": "Not applicable",
};

function pluralize(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function clinicianName(clinicians: Clinician[], clinicianId: string) {
  return clinicians.find((clinician) => clinician.id === clinicianId)?.displayName ?? "Fictional clinician";
}

export function deliveryStateLabel(deliveryState: DeliveryState) {
  return deliveryLabels[deliveryState];
}

export function ResponseSummary({
  alert,
  clinicians,
}: {
  alert: Pick<AlertRecord, "deliveryState" | "recipients">;
  clinicians: Clinician[];
}) {
  const accepted = alert.recipients.filter((recipient) => recipient.response === "accepted");
  const acknowledged = alert.recipients.filter((recipient) => recipient.response === "acknowledged");
  const declinedOrUnavailable = alert.recipients.filter(
    (recipient) => recipient.response === "declined" || recipient.response === "unavailable",
  );
  const noResponse = alert.recipients.filter((recipient) => recipient.response === "none");

  const groups = [
    {
      label: "Accepted",
      value: `${accepted.length} accepted responsibility`,
      recipients: accepted,
    },
    {
      label: "Acknowledged",
      value: `${acknowledged.length} acknowledged receipt`,
      recipients: acknowledged,
    },
    {
      label: "Declined / unavailable",
      value: `${declinedOrUnavailable.length} declined or unavailable`,
      recipients: declinedOrUnavailable,
    },
    {
      label: "No response",
      value: `${pluralize(noResponse.length, "no response", "no response")} yet`,
      recipients: noResponse,
    },
  ];

  return (
    <section className="response-summary detail-card" role="region" aria-label="Responses Summary">
      <div className="section-heading">
        <h2>Responses Summary</h2>
      </div>
      <dl className="response-summary__delivery">
        <div>
          <dt>Delivery state</dt>
          <dd>{deliveryStateLabel(alert.deliveryState)}</dd>
        </div>
      </dl>
      <div className="response-summary__grid">
        {groups.map((group) => (
          <article className="response-summary__item" key={group.label}>
            <h3>{group.label}</h3>
            <p>{group.value}</p>
            {group.recipients.length > 0 ? (
              <ul>
                {group.recipients.map((recipient) => (
                  <li key={`${group.label}-${recipient.clinicianId}`}>
                    {clinicianName(clinicians, recipient.clinicianId)}
                  </li>
                ))}
              </ul>
            ) : null}
          </article>
        ))}
      </div>
      <p className="response-summary__note">
        Acknowledgement confirms receipt only; it does not accept responsibility.
      </p>
    </section>
  );
}
