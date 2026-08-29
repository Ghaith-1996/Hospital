import React from "react";
import { SimulationChrome } from "./simulation-chrome";

export default function HomePage() {
  return (
    <SimulationChrome
      title="Critical Alerts Platform"
      lead="Phase 6 adds manual recipient selection, protected approved-message editing, exact human review, and simulation-only confirmation on top of the fictional directory boundary."
    >
      <p id="phase-boundary">
        Confirmation creates an identifier-only outbox request for a future simulation phase; this phase does
        not process it, call providers, create deliveries, retry, escalate, or expose a live dispatch screen.
        CSV remains a fictional directory adapter, not a hospital directory connection, and all alert content
        remains simulation-only.
      </p>
      <a className="focus-link" href="#phase-boundary">
        Review the Phase 6 boundary
      </a>
    </SimulationChrome>
  );
}
