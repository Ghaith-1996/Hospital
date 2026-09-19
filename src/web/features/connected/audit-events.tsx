"use client";
import React from "react";
import { auditActions, auditOutcomes, auditResourceTypes, AuditClientError, queryAudit, type AuditFilters, type AuditPage } from "../../lib/audit";

export function AuditEvents() {
  const [filters, setFilters] = React.useState<AuditFilters>({});
  const [request, setRequest] = React.useState<{ filters: AuditFilters; cursors: Array<string | null>; revision: number }>({ filters: {}, cursors: [null], revision: 0 });
  const [page, setPage] = React.useState<AuditPage | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const status = React.useRef<HTMLParagraphElement>(null);
  React.useEffect(() => {
    let cancelled = false;
    queryAudit(request.filters, request.cursors[request.cursors.length - 1]).then(result => {
      if (!cancelled) { setPage(result); setLoading(false); setError(null); }
    }, failure => {
      if (!cancelled) { setPage(null); setLoading(false); setError(failure instanceof AuditClientError ? failure.message : "Audit service unavailable. Retry the query."); }
    });
    return () => { cancelled = true; };
  }, [request]);
  function load(next: typeof request) { setLoading(true); setError(null); setPage(null); setRequest(next); }
  function change(key: keyof AuditFilters, value: string) { setFilters(current => ({ ...current, [key]: value })); }
  function timeValue(value: string | undefined) { return value?.replace(/Z$/, "") ?? ""; }
  return <div className="page-stack">
    <header><h1>Audit</h1><p>Organization-scoped simulation audit. Clinical content is excluded.</p></header>
    <form className="detail-card" aria-label="Audit filters" onSubmit={event => { event.preventDefault(); load({ filters: { ...filters }, cursors: [null], revision: request.revision + 1 }); }}>
      <div className="detail-grid">
        <label className="filter-field">From (UTC)<input type="datetime-local" value={timeValue(filters.occurredFromUtc)} onChange={event => change("occurredFromUtc", event.target.value ? event.target.value + "Z" : "")} /></label>
        <label className="filter-field">To (UTC)<input type="datetime-local" value={timeValue(filters.occurredToUtc)} onChange={event => change("occurredToUtc", event.target.value ? event.target.value + "Z" : "")} /></label>
        {([["action", "Action", auditActions], ["outcome", "Outcome", auditOutcomes], ["resourceType", "Resource type", auditResourceTypes]] as const).map(([key, label, choices]) =>
          <label className="filter-field" key={key}>{label}<select value={filters[key] ?? ""} onChange={event => change(key, event.target.value)}><option value="">All</option>{choices.map(value => <option key={value} value={value}>{value}</option>)}</select></label>)}
        <label className="filter-field">Correlation ID<input maxLength={36} autoComplete="off" value={filters.correlationId ?? ""} onChange={event => change("correlationId", event.target.value)} /></label>
      </div>
      <button type="submit" disabled={loading}>Apply filters</button>
    </form>
    <p role="status" ref={status} tabIndex={-1}>{loading ? "Loading audit events…" : error ? "Audit query failed." : `Page ${request.cursors.length} · ${page?.events.length ?? 0} events`}</p>
    {error && <div role="alert" className="error-panel"><p>{error}</p><button type="button" onClick={() => load({ ...request, revision: request.revision + 1 })}>Retry audit query</button></div>}
    {page && !loading && (page.events.length === 0 ? <p>No audit events match these filters.</p> : <div className="table-wrap">
      <table aria-label="Audit events"><thead><tr>{["Time (UTC)", "Action", "Resource type", "Resource identifier", "Actor type", "Outcome", "Correlation ID", "Safe metadata"].map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead>
        <tbody>{page.events.map(row => <tr key={row.id}>
          <td><time dateTime={row.occurredAtUtc}>{row.occurredAtUtc}</time></td><td>{row.action}</td><td>{row.resourceType}</td><td>{row.resourceId}</td><td>{row.actorType}</td><td>{row.outcome}</td><td>{row.correlationId ?? "Unavailable"}</td>
          <td>{Object.entries(row.metadata).map(([key, value]) => `${key}: ${Array.isArray(value) ? value.join(", ") : String(value)}`).join("; ") || "None"}</td>
        </tr>)}</tbody></table>
    </div>)}
    <nav className="form-actions" aria-label="Audit pagination">
      <button type="button" className="button-secondary" disabled={loading || request.cursors.length === 1} onClick={() => { load({ ...request, cursors: request.cursors.slice(0, -1) }); status.current?.focus(); }}>Previous page</button>
      <button type="button" disabled={loading || !page?.nextCursor} onClick={() => { if (page?.nextCursor) load({ ...request, cursors: [...request.cursors, page.nextCursor] }); status.current?.focus(); }}>Next page</button>
    </nav>
  </div>;
}
