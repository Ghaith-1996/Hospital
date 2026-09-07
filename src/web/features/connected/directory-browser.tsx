"use client";
import React from "react";
import * as api from "../../lib/alerts";
import { ApiError } from "./common";

export function DirectoryEvidence({ person }: { person: api.DirectoryPractitioner }) {
  return <div className="clinician-row__body"><strong>{person.displayName}</strong>
    <p>{person.simulationCode} · {person.specialty} · {person.department ?? "Department unavailable"} · {person.site ?? "Site unavailable"} · {person.roleTitle ?? "Role unavailable"}</p>
    <p>{person.isActive ? "Active" : "Inactive"} · {person.isStale ? "Stale — review freshness" : "Current"}</p>
    <p>Directory: {person.sourceSystem ?? "Unknown"} · Synchronized: {person.lastSynchronizedAtUtc ?? "Unknown"}</p>
    <p>On-call: {person.onCallTier ?? "Not on call"} · Source: {person.onCallSourceSystem ?? "Unknown"} · Synchronized: {person.onCallLastSynchronizedAtUtc ?? "Unknown"}</p>
    <p>Available simulated channels: {person.availableChannels.join(", ") || "None"}</p>
  </div>;
}

export function DirectoryBrowser({ initial, controls, onResults }: { initial: api.DirectoryPractitioner[]; controls?: (person: api.DirectoryPractitioner) => React.ReactNode; onResults?: (people: api.DirectoryPractitioner[]) => void }) {
  const [results, setResults] = React.useState(initial);
  const [filters, setFilters] = React.useState({ text: "", department: "", site: "", onCallNow: false });
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  return <section className="clinician-selector"><h2>Fictional Directory</h2>
    <form className="form-grid" onSubmit={async event => {
      event.preventDefault(); setBusy(true); setError(null);
      try { const people = await api.searchDirectory({ ...filters, onCallNow: filters.onCallNow || undefined, includeInactive: true }); setResults(people); onResults?.(people); }
      catch (failure) { setError(failure); } finally { setBusy(false); }
    }}>
      <label className="filter-field">Search name or specialty<input value={filters.text} onChange={event => setFilters({ ...filters, text: event.target.value })} /></label>
      <label className="filter-field">Department filter<input value={filters.department} onChange={event => setFilters({ ...filters, department: event.target.value })} /></label>
      <label className="filter-field">Site filter<input value={filters.site} onChange={event => setFilters({ ...filters, site: event.target.value })} /></label>
      <label className="confirmation-check"><input type="checkbox" checked={filters.onCallNow} onChange={event => setFilters({ ...filters, onCallNow: event.target.checked })} />On call now</label>
      <button type="submit" disabled={busy}>{busy ? "Searching…" : "Search directory"}</button>
    </form><ApiError error={error} />
    <div className="clinician-list"><h3>Search Results</h3>{results.length === 0 && <p>No matching fictional practitioners.</p>}
      <ul>{results.map(person => <li className="clinician-row" key={person.practitionerId}><span className="clinician-avatar" aria-hidden="true">{person.firstName[0]}{person.lastName[0]}</span><DirectoryEvidence person={person} />{controls?.(person)}</li>)}</ul>
    </div>
  </section>;
}
