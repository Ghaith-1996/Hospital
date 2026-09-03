"use client";

import React from "react";
import { MoreIcon } from "../ui/icons";
import type { DoctorResponse } from "../../features/alerts/types";

type ResponseAction = Exclude<DoctorResponse, "none">;

const responseActions: Array<{ value: ResponseAction; label: string }> = [
  { value: "acknowledged", label: "Acknowledge" },
  { value: "accepted", label: "Accept" },
  { value: "declined", label: "Decline" },
  { value: "unavailable", label: "Unavailable" },
];

const responseLabels: Record<DoctorResponse, string> = {
  none: "No response yet",
  acknowledged: "Acknowledged",
  accepted: "Accepted",
  declined: "Declined",
  unavailable: "Unavailable",
};

export function responseLabel(response: DoctorResponse) {
  return responseLabels[response];
}

export function ResponsePanel({
  alertId,
  currentResponse,
  onChoose,
}: {
  alertId: string;
  currentResponse: DoctorResponse;
  onChoose(response: ResponseAction): void;
}) {
  return (
    <section className="response-panel response-panel--sticky" role="region" aria-label="Respond to this fictional alert">
      <div className="response-panel__summary">
        <span>Respond:</span>
        <strong>{responseLabel(currentResponse)}</strong>
      </div>
      <div className="response-panel__actions" aria-label={`Response actions for ${alertId}`}>
        {responseActions.map((action) => (
          <button
            className={`response-action response-action--${action.value}`}
            key={action.value}
            type="button"
            aria-pressed={currentResponse === action.value}
            onClick={() => onChoose(action.value)}
          >
            {action.label}
          </button>
        ))}
        <button className="response-action response-action--more" type="button" title="Coming later" aria-label="More">
          <MoreIcon />
        </button>
      </div>
    </section>
  );
}
