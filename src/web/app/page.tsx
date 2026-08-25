import React from "react";
import { SimulationChrome } from "./simulation-chrome";

export default function HomePage() {
  return (
    <SimulationChrome
      title="Critical Alerts Platform"
      lead="Phase 4 adds a fictional practitioner directory and CSV import adapter. It is not a hospital directory connection."
    >
      <p id="phase-boundary">
        Alert drafting, recipient dispatch, clinical content, provider delivery, and escalation remain
        intentionally unavailable in this phase. CSV is the first directory adapter, not the directory
        model.
      </p>
      <a className="focus-link" href="#phase-boundary">
        Review the Phase 4 boundary
      </a>
    </SimulationChrome>
  );
}
