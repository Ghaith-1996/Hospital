"use client";
import React from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import * as api from "../../lib/alerts";
import { PageHeader } from "../../components/ui/page-header";
import { ApiError, Loading, useServerQuery, useUnsavedChanges } from "./common";
import { DirectoryBrowser, DirectoryEvidence } from "./directory-browser";

export function RecipientSelection({ alertId }: { alertId: string }) {
  const load = React.useCallback(async () => ({ draft: await api.getAlertDraft(alertId), people: await api.searchDirectory({ includeInactive: true }) }), [alertId]);
  const query = useServerQuery(load);
  const [revision, setRevision] = React.useState(0);
  const reload = () => {
    const guard = new Event("workflow:before-leave", { cancelable: true });
    if (window.dispatchEvent(guard)) void query.refresh().then(() => setRevision(value => value + 1)).catch(() => {});
  };
  return <><PageHeader title="Select Recipients" description="Manually review each fictional clinician and notification channel." /><ApiError error={query.error} retry={reload} />
    {query.loading ? <Loading /> : query.data && <RecipientsForm key={`${query.data.draft.draftVersion}-${revision}`} {...query.data} reload={reload} />}</>;
}
function RecipientsForm({ draft, people, reload }: { draft: api.AlertDraft; people: api.DirectoryPractitioner[]; reload(): void }) {
  const initial = draft.recipients.map(({ practitionerId, practitionerRoleId, channel, directoryRevision }) => ({ practitionerId, practitionerRoleId, channel, directoryRevision }));
  const [selected, setSelected] = React.useState<api.AlertRecipientInput[]>(initial);
  const [known, setKnown] = React.useState(people);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  const lock = React.useRef(false);
  const router = useRouter();
  const releaseDirty = useUnsavedChanges(JSON.stringify(selected) !== JSON.stringify(initial));
  const invalid = selected.some(item => !item.channel || !known.find(person => person.practitionerId === item.practitionerId)?.selectable);
  return <div className="new-alert-layout"><div className="new-alert-form"><p>Draft version {draft.draftVersion}</p><ApiError error={error} retry={reload} />
    <DirectoryBrowser initial={people} onResults={results => setKnown(current => [...current.filter(person => !results.some(result => result.practitionerId === person.practitionerId)), ...results])} controls={person => <label className="confirmation-check"><input type="checkbox" aria-label={`Select ${person.displayName} ${person.simulationCode}`} disabled={!person.selectable || busy} checked={selected.some(item => item.practitionerId === person.practitionerId)} onChange={event => setSelected(event.target.checked ? [...selected, { practitionerId: person.practitionerId, practitionerRoleId: person.practitionerRoleId, channel: "", directoryRevision: person.selectionRevision }] : selected.filter(item => item.practitionerId !== person.practitionerId))} />Select</label>} />
    <Link href={`/alerts/${draft.alertId}/compose`}>Back to compose</Link>
  </div><aside className="alert-summary"><h2>Selected Clinicians ({selected.length})</h2>
    {selected.map((item, index) => {
      const person = known.find(candidate => candidate.practitionerId === item.practitionerId);
      return <section className="detail-card" key={`${item.practitionerId}-${index}`}>
        {person ? <><DirectoryEvidence person={person} />{person.selectionRevision !== item.directoryRevision && <p role="alert">Directory revision changed. Remove and reselect this clinician after reviewing the new evidence.</p>}
          <label className="filter-field">Channel for {person.displayName} {person.simulationCode}<select value={item.channel} disabled={busy} onChange={event => setSelected(selected.map((entry, i) => i === index ? { ...entry, channel: event.target.value } : entry))}><option value="">Choose a simulated channel</option>{person.availableChannels.map(channel => <option key={channel}>{channel}</option>)}</select></label></> : <p>Practitioner inaccessible. Remove this selection.</p>}
        <button type="button" className="button-secondary" disabled={busy} onClick={() => setSelected(selected.filter((_, i) => i !== index))}>Remove {person?.displayName ?? "selection"}</button>
      </section>;
    })}
    <p>Saving increments the draft version and requires explicit critical-field reconfirmation.</p>
    <button type="button" disabled={busy || invalid} onClick={async () => {
      if (lock.current) return; lock.current = true; setBusy(true); setError(null);
      try { await api.replaceAlertRecipients(draft.alertId, draft.draftVersion, selected); releaseDirty(); router.push(`/alerts/${draft.alertId}/compose`); }
      catch (failure) { setError(failure); } finally { lock.current = false; setBusy(false); }
    }}>Save recipients and reconfirm fields</button>
  </aside></div>;
}
