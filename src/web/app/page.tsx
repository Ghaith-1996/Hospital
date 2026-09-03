import React from "react";
import { SimulationChrome } from "./simulation-chrome";

export default function HomePage() {
  return (
    <SimulationChrome
      title="Critical Alerts Platform"
      lead="Phase 8 adds a server-scoped practitioner inbox and safe operator status projection to the simulation-only alert workflow."
    >
      <p id="phase-boundary">
        Fictional adapters process identifier-only dispatch requests, and linked practitioners can record open,
        acknowledgement, and one terminal simulation disposition. The refreshed operator view is not guaranteed
        real-time monitoring. No real provider, callback, escalation, alert resolution, hospital integration, or AI
        behavior is enabled.
      </p>
      <a className="focus-link" href="#phase-boundary">
        Review the Phase 8 boundary
      </a>
    </SimulationChrome>
  );
}
