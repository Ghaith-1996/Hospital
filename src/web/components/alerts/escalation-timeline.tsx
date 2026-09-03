import React from "react";
import type { EscalationStep } from "../../features/alerts/types";

const stepStateLabels: Record<EscalationStep["state"], string> = {
  complete: "Complete",
  active: "In progress",
  pending: "Pending",
};

export function EscalationTimeline({ steps }: { steps?: EscalationStep[] }) {
  return (
    <section className="escalation-timeline detail-card" role="region" aria-label="Alert Escalation">
      <div className="section-heading">
        <h2>Escalation Progress</h2>
      </div>
      {(steps ?? []).length > 0 ? (
        <ol className="escalation-list" aria-label="Alert Escalation">
          {(steps ?? []).map((step) => (
            <li className={`escalation-step escalation-step--${step.state}`} key={step.id}>
              <span className="escalation-step__state">{stepStateLabels[step.state]}</span>
              <div>
                <h3>{step.label}</h3>
                <p>{step.detail}</p>
                <span>{step.atLabel}</span>
              </div>
            </li>
          ))}
        </ol>
      ) : (
        <p className="empty-note">No fictional escalation steps are configured for this alert.</p>
      )}
    </section>
  );
}
