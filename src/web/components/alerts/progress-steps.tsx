import React from "react";

const steps = ["New Alert", "Review & Confirm", "Alert Sent"] as const;

export function ProgressSteps({ current }: { current: 1 | 2 | 3 }) {
  return (
    <ol className="progress-steps" aria-label="Alert creation progress">
      {steps.map((label, index) => {
        const stepNumber = (index + 1) as 1 | 2 | 3;
        const state = stepNumber < current ? "complete" : stepNumber === current ? "current" : "pending";
        const statusLabel = state === "complete" ? "completed" : state === "current" ? "current step" : "not started";

        return (
          <li
            className={`progress-steps__item progress-steps__item--${state}`}
            key={label}
            aria-current={state === "current" ? "step" : undefined}
          >
            <span className="progress-steps__marker" aria-hidden="true">
              {stepNumber}
            </span>
            <span className="progress-steps__label">
              {label}
              <span className="sr-only">, {statusLabel}</span>
            </span>
          </li>
        );
      })}
    </ol>
  );
}
