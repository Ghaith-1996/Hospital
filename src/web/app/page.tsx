import React from "react";

export default function HomePage() {
  return (
    <main className="page-shell">
      <section className="status-card" aria-labelledby="page-title">
        <p className="simulation-banner" role="status" aria-label="SIMULATION MODE">
          SIMULATION MODE
        </p>
        <h1 id="page-title">Critical Alerts Platform</h1>
        <p className="lead">
          Phase 1 is a local platform scaffold for a fictional healthcare communication
          simulation.
        </p>
        <p id="phase-boundary">
          Alert drafting, recipients, clinical content, provider delivery, and escalation are
          intentionally unavailable in this phase.
        </p>
        <a className="focus-link" href="#phase-boundary">
          Review the Phase 1 boundary
        </a>
      </section>
    </main>
  );
}
