import React from "react";
import { DevelopmentAuthPanel } from "./development-auth-panel";

export default function HomePage() {
  return (
    <main className="page-shell">
      <section className="status-card" aria-labelledby="page-title">
        <p className="simulation-banner" role="status" aria-label="SIMULATION MODE">
          SIMULATION MODE
        </p>
        <p className="development-auth-banner" role="status" aria-label="DEVELOPMENT AUTHENTICATION">
          DEVELOPMENT AUTHENTICATION
        </p>
        <h1 id="page-title">Critical Alerts Platform</h1>
        <p className="lead">
          Phase 3 adds fictional development authentication for a healthcare communication
          simulation. It is not hospital SSO.
        </p>
        <p id="phase-boundary">
          Alert drafting, recipients, clinical content, provider delivery, and escalation remain
          intentionally unavailable in this phase.
        </p>
        <DevelopmentAuthPanel />
        <a className="focus-link" href="#phase-boundary">
          Review the Phase 3 boundary
        </a>
      </section>
    </main>
  );
}
