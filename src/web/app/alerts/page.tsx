"use client";
import React from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { PageHeader } from "../../components/ui/page-header";
export default function AlertsPage() {
  const [id, setId] = React.useState("");
  const [screen, setScreen] = React.useState("live");
  const router = useRouter();
  return <><PageHeader title="Alerts" description="Open a saved simulation alert by its opaque identifier." actions={<Link className="button-primary" href="/alerts/new">New alert</Link>} /><form className="detail-card" onSubmit={event => { event.preventDefault(); if (/^[0-9a-f-]{36}$/i.test(id)) router.push(`/alerts/${id}/${screen}`); }}><p>Use the identifier from the saved draft or live-status URL. The Phase 0–8 API does not expose an operator list endpoint.</p><label className="filter-field">Alert identifier<input required pattern="[0-9a-fA-F\-]{36}" value={id} onChange={event => setId(event.target.value)} /></label><label className="filter-field">View<select value={screen} onChange={event => setScreen(event.target.value)}><option value="live">Live status</option><option value="compose">Saved draft</option></select></label><button type="submit">Open saved alert</button></form></>;
}
