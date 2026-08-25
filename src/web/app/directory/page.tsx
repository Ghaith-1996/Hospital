"use client";

import React, { FormEvent, useEffect, useState } from "react";
import { SimulationChrome } from "../simulation-chrome";

type DirectoryPractitioner = {
  practitionerId: string;
  displayName: string;
  specialty: string;
  department: string | null;
  site: string | null;
  roleTitle: string | null;
  simulationCode: string;
  isActive: boolean;
  isStale: boolean;
  selectable: boolean;
  sourceSystem: string | null;
  lastSynchronizedAtUtc: string | null;
  onCallTier: string | null;
  onCallSourceSystem: string | null;
  onCallLastSynchronizedAtUtc: string | null;
};

export default function DirectoryPage() {
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("Sign in with a seeded Operator or Administrator identity to search.");
  const [results, setResults] = useState<DirectoryPractitioner[]>([]);

  async function search(event?: FormEvent) {
    event?.preventDefault();
    const response = await fetch(`/api/directory/practitioners?q=${encodeURIComponent(query)}`, {
      credentials: "include",
    });
    if (response.status === 401) {
      setResults([]);
      setStatus("Sign in with a seeded Operator or Administrator identity to search.");
      return;
    }
    if (response.status === 403) {
      setResults([]);
      setStatus("Practitioner identities can view this simulation shell but cannot read the directory.");
      return;
    }
    if (!response.ok) {
      setResults([]);
      setStatus("The directory search could not be completed.");
      return;
    }

    const loaded = (await response.json()) as DirectoryPractitioner[];
    setResults(loaded);
    setStatus(
      loaded.length === 0
        ? "No fictional practitioners matched that search."
        : "Similar names are listed separately. Inactive rows are not selectable. Matching never uses display name alone.",
    );
  }

  useEffect(() => {
    void fetch("/api/directory/practitioners", { credentials: "include" }).then(async (response) => {
      if (response.status === 401) {
        setResults([]);
        setStatus("Sign in with a seeded Operator or Administrator identity to search.");
        return;
      }
      if (response.status === 403) {
        setResults([]);
        setStatus("Practitioner identities can view this simulation shell but cannot read the directory.");
        return;
      }
      if (!response.ok) {
        setResults([]);
        setStatus("The directory search could not be completed.");
        return;
      }

      const loaded = (await response.json()) as DirectoryPractitioner[];
      setResults(loaded);
      setStatus(
        loaded.length === 0
          ? "No fictional practitioners matched that search."
          : "Similar names are listed separately. Inactive rows are not selectable. Matching never uses display name alone.",
      );
    });
  }, []);

  return (
    <SimulationChrome
      title="Fictional practitioner directory"
      lead="Search disambiguates similar names with specialty, department, site, role, synthetic identifier, activity, freshness, and on-call source. This is not recipient dispatch."
    >
      <form className="directory-search" onSubmit={search}>
        <label htmlFor="directory-query">Search practitioners</label>
        <input
          id="directory-query"
          name="q"
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Name, specialty, or SIM-PRAC code"
        />
        <button type="submit">Search</button>
      </form>
      <p>{status}</p>
      <div className="table-wrap">
        <table className="directory-table">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Specialty</th>
              <th scope="col">Department</th>
              <th scope="col">Site</th>
              <th scope="col">Role</th>
              <th scope="col">Synthetic ID</th>
              <th scope="col">Status</th>
              <th scope="col">Source</th>
              <th scope="col">On-call</th>
              <th scope="col">Selectable</th>
            </tr>
          </thead>
          <tbody>
            {results.map((practitioner) => (
              <tr key={practitioner.practitionerId}>
                <td>{practitioner.displayName}</td>
                <td>{practitioner.specialty}</td>
                <td>{practitioner.department ?? "—"}</td>
                <td>{practitioner.site ?? "—"}</td>
                <td>{practitioner.roleTitle ?? "—"}</td>
                <td>{practitioner.simulationCode}</td>
                <td>
                  {practitioner.isActive ? "Active" : "Inactive"}
                  {practitioner.isStale ? " / Stale" : ""}
                </td>
                <td>
                  {practitioner.sourceSystem ?? "—"}
                  {practitioner.lastSynchronizedAtUtc
                    ? ` @ ${new Date(practitioner.lastSynchronizedAtUtc).toISOString()}`
                    : ""}
                </td>
                <td>
                  {practitioner.onCallTier
                    ? `${practitioner.onCallTier} (${practitioner.onCallSourceSystem ?? "unknown source"})`
                    : "—"}
                </td>
                <td>{practitioner.selectable ? "Yes" : "No"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </SimulationChrome>
  );
}
