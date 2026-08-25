"use client";

import React from "react";
import Link from "next/link";
import { DevelopmentAuthPanel } from "./development-auth-panel";

export function SimulationChrome({
  title,
  lead,
  children,
}: {
  title: string;
  lead: string;
  children: React.ReactNode;
}) {
  return (
    <main className="page-shell page-shell-wide">
      <section className="status-card">
        <p className="simulation-banner" role="status" aria-label="SIMULATION MODE">
          SIMULATION MODE
        </p>
        <p className="development-auth-banner" role="status" aria-label="DEVELOPMENT AUTHENTICATION">
          DEVELOPMENT AUTHENTICATION
        </p>
        <nav className="page-nav" aria-label="Simulation navigation">
          <Link className="focus-link" href="/">
            Home
          </Link>
          <Link className="focus-link" href="/directory">
            Practitioner directory
          </Link>
          <Link className="focus-link" href="/directory/import">
            Directory import
          </Link>
        </nav>
        <h1>{title}</h1>
        <p className="lead">{lead}</p>
        <DevelopmentAuthPanel />
        {children}
      </section>
    </main>
  );
}
