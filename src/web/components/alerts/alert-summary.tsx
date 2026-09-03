import React from "react";
import type { Clinician, Urgency } from "../../features/alerts/types";

export type AlertSummaryProps = {
  patientReference: string;
  urgency: Urgency;
  caseDetails: string;
  selectedClinicians: Clinician[];
};

function formatUrgency(urgency: Urgency) {
  if (urgency === "critical") return "Critical";
  if (urgency === "high") return "High";
  return "Routine";
}

export function AlertSummary({ patientReference, urgency, caseDetails, selectedClinicians }: AlertSummaryProps) {
  return (
    <aside className="alert-summary" data-testid="alert-summary" aria-labelledby="alert-summary-heading">
      <h2 id="alert-summary-heading">Alert Summary</h2>
      <dl className="summary-list">
        <div>
          <dt>Patient reference</dt>
          <dd>{patientReference.trim() || "Not specified"}</dd>
        </div>
        <div>
          <dt>Urgency</dt>
          <dd>{formatUrgency(urgency)}</dd>
        </div>
        <div>
          <dt>Case details</dt>
          <dd>{caseDetails.trim() || "Not specified"}</dd>
        </div>
        <div>
          <dt>Clinicians</dt>
          <dd>
            {selectedClinicians.length > 0 ? (
              <ul className="summary-clinicians">
                {selectedClinicians.map((clinician) => (
                  <li key={clinician.id}>{clinician.displayName}</li>
                ))}
              </ul>
            ) : (
              "None selected"
            )}
          </dd>
        </div>
      </dl>
    </aside>
  );
}
