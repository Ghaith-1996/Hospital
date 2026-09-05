"use client";
import React from "react";
import { useRouter } from "next/navigation";
import { createAlertDraft } from "../../lib/alerts";
import { getSimulationLocationContext } from "../../lib/development-auth";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Field, Loading, useServerQuery, useUnsavedChanges } from "./common";
import { DraftFields, emptyContent } from "./draft-fields";

export function NewAlert() {
  const query = useServerQuery(getSimulationLocationContext);
  const [siteId, setSiteId] = React.useState("");
  const [departmentId, setDepartmentId] = React.useState("");
  const [patient, setPatient] = React.useState("");
  const [content, setContent] = React.useState(emptyContent);
  const [error, setError] = React.useState<unknown>(null);
  const [busy, setBusy] = React.useState(false);
  const lock = React.useRef(false);
  const releaseDirty = useUnsavedChanges(!!patient || JSON.stringify(content) !== JSON.stringify(emptyContent));
  const router = useRouter();
  const departments = query.data?.sites.find(site => site.siteId === siteId)?.departments ?? [];
  return <><PageHeader title="Alert Doctor" description="Create a fictional alert with human-entered source and SBAR." />
    <ApiError error={query.error} retry={query.reload} />
    {query.loading ? <Loading /> : query.data && <form className="new-alert-layout" onSubmit={async event => {
      event.preventDefault(); if (lock.current) return;
      lock.current = true; setBusy(true); setError(null);
      try { const draft = await createAlertDraft({ siteId, departmentId, simulationPatientReference: patient, ...content }); releaseDirty(); router.push(`/alerts/${draft.alertId}/compose`); }
      catch (failure) { setError(failure); }
      finally { lock.current = false; setBusy(false); }
    }}>
      <fieldset className="new-alert-form" disabled={busy}><div className="section-heading"><h2>New Alert</h2><span className="simulation-pill">SIMULATION MODE</span></div>
        <ApiError error={error} />
        <label className="filter-field">Simulation site<select required value={siteId} onChange={event => { setSiteId(event.target.value); setDepartmentId(""); }}><option value="">Select a fictional site</option>{query.data.sites.map(site => <option value={site.siteId} key={site.siteId}>{site.name}</option>)}</select></label>
        <label className="filter-field">Simulation department<select required value={departmentId} onChange={event => setDepartmentId(event.target.value)}><option value="">Select a fictional department</option>{departments.map(department => <option value={department.departmentId} key={department.departmentId}>{department.name}</option>)}</select></label>
        <Field label="Fictional patient reference" value={patient} onChange={setPatient} />
        <DraftFields value={content} onChange={setContent} />
        <div className="form-actions"><button type="button" className="button-secondary" onClick={() => { setPatient(""); setContent(emptyContent); setSiteId(""); setDepartmentId(""); }}>Discard unsaved edits</button><button type="submit">{busy ? "Creating…" : "Create backend draft"}</button></div>
      </fieldset>
      <aside className="alert-summary"><h2>Alert Summary</h2><p>{patient || "Fictional patient reference required"}</p><p>{content.location || "Fictional location required"}</p><p>{content.urgencyLabel || "Select the DEMO urgency deliberately"}</p><p>Save a draft, confirm its critical fields, manually select recipients and channels, then review the exact version before dispatch.</p><p>Fictional data only. Notifications are simulated.</p></aside>
    </form>}
  </>;
}
