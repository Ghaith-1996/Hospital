import React from "react";
import { SimulationChrome } from "./simulation-chrome";

export default function HomePage() {
  return (
    <SimulationChrome
      title="Critical Alerts Platform"
      lead="Phase 5 adds simulation-only typed alert drafting on top of the fictional directory boundary. It is not a hospital connection or dispatch system."
    >
      <p id="phase-boundary">
        Recipient selection, provider delivery, dispatch, and escalation remain intentionally unavailable
        in this phase. CSV is the first directory adapter, not the directory model, and draft clinical text
        is simulation-only.
      </p>
      <a className="focus-link" href="#phase-boundary">
        Review the Phase 4 boundary
      </a>
    </SimulationChrome>
  );
}
