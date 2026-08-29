"use client";

import { FormEvent, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { SimulationChrome } from "../../../simulation-chrome";
import {
  AlertApiError,
  AlertDraft,
  AlertRecipientInput,
  DirectoryPractitioner,
  getAlertDraft,
  isAlertApiError,
  replaceAlertRecipients,
  searchDirectory,
} from "../../../../lib/alerts";

function routeAlertId(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function recipientKey(practitionerId: string, channel: string): string {
  return `${practitionerId}:${channel}`;
}

function safeUtc(value: string | null): string {
  if (!value) {
    return "not available";
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.valueOf()) ? "not available" : parsed.toISOString();
}

function safeErrorStatus(error: unknown, fallback: string): string {
  if (isAlertApiError(error) && error.status === 401) {
    return "Sign in with a seeded Operator or Administrator identity to select recipients.";
  }
  if (isAlertApiError(error) && error.status === 403) {
    return "Practitioner identities cannot select alert recipients.";
  }
  return fallback;
}

function isDirectoryRevisionConflict(error: unknown): error is AlertApiError {
  return isAlertApiError(error) && error.status === 409;
}

export default function AlertRecipientsPage() {
  const params = useParams<{ id: string | string[] }>();
  const router = useRouter();
  const alertId = routeAlertId(params.id);
  const [draft, setDraft] = useState<AlertDraft | null>(null);
  const [loadedAlertId, setLoadedAlertId] = useState<string | null>(null);
  const [results, setResults] = useState<DirectoryPractitioner[]>([]);
  const [selected, setSelected] = useState<Record<string, AlertRecipientInput>>({});
  const [text, setText] = useState("");
  const [department, setDepartment] = useState("");
  const [site, setSite] = useState("");
  const [onCallFilter, setOnCallFilter] = useState("any");
  const [status, setStatus] = useState("Loading the fictional directory and current recipient set.");
  const [searching, setSearching] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let active = true;
    void Promise.all([
      getAlertDraft(alertId),
      searchDirectory({ includeInactive: false }),
    ])
      .then(([loadedDraft, directory]) => {
        if (!active) {
          return;
        }
        setLoadedAlertId(alertId);
        setDraft(loadedDraft);
        setResults(directory);
        setSelected(
          Object.fromEntries(
            loadedDraft.recipients.map((recipient) => [
              recipientKey(recipient.practitionerId, recipient.channel),
              {
                practitionerId: recipient.practitionerId,
                practitionerRoleId: recipient.practitionerRoleId,
                channel: recipient.channel,
                directoryRevision: recipient.directoryRevision,
              },
            ]),
          ),
        );
        setStatus("Directory results loaded. Nothing is selected automatically for a new recipient set.");
      })
      .catch((error: unknown) => {
        if (active) {
          setLoadedAlertId(alertId);
          setStatus(safeErrorStatus(error, "The fictional directory could not be loaded. Reload and try again."));
        }
      });

    return () => {
      active = false;
    };
  }, [alertId]);

  const selectedCount = useMemo(() => Object.keys(selected).length, [selected]);

  async function search(event?: FormEvent<HTMLFormElement>) {
    event?.preventDefault();
    setSearching(true);
    try {
      const directory = await searchDirectory({
        text,
        department,
        site,
        onCallNow: onCallFilter === "true" ? true : undefined,
        includeInactive: false,
      });
      setResults(directory);
      setStatus("Directory results refreshed. Check each channel before saving the complete set.");
    } catch (error) {
      setStatus(safeErrorStatus(error, "The fictional directory search could not be completed."));
    } finally {
      setSearching(false);
    }
  }

  function toggleRecipient(practitioner: DirectoryPractitioner, channel: string, checked: boolean) {
    const key = recipientKey(practitioner.practitionerId, channel);
    setSelected((current) => {
      const next = { ...current };
      if (checked) {
        next[key] = {
          practitionerId: practitioner.practitionerId,
          practitionerRoleId: practitioner.practitionerRoleId,
          channel,
          directoryRevision: practitioner.selectionRevision,
        };
      } else {
        delete next[key];
      }
      return next;
    });
  }

  async function saveRecipientSet(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || draft.state !== "Draft") {
      return;
    }
    setSaving(true);
    try {
      await replaceAlertRecipients(alertId, draft.draftVersion, Object.values(selected));
      setStatus("Recipient set saved. Critical fields must be reconfirmed for the new version.");
      router.push(`/alerts/${alertId}/compose?recipientsSaved=1`);
    } catch (error) {
      setStatus(
        isDirectoryRevisionConflict(error)
          ? "The directory changed. Reload the results and reselect recipients."
          : safeErrorStatus(error, "The recipient set could not be saved. Review each selected channel and retry."),
      );
    } finally {
      setSaving(false);
    }
  }

  if (loadedAlertId !== alertId) {
    return (
      <SimulationChrome
        title="Select fictional recipients"
        lead="Load the fictional directory and the current recipient snapshot for this simulation alert."
      >
        <p className="status-message" role="status" aria-live="polite">
          {status}
        </p>
      </SimulationChrome>
    );
  }

  if (!draft) {
    return (
      <SimulationChrome
        title="Select fictional recipients"
        lead="The simulation draft could not be loaded for recipient selection."
      >
        <p className="status-message" role="status" aria-live="polite">
          {status}
        </p>
        <Link className="focus-link" href="/alerts/new">
          Create another simulation draft
        </Link>
      </SimulationChrome>
    );
  }

  const canEdit = draft.state === "Draft";

  return (
    <SimulationChrome
      title="Select fictional recipients"
      lead="Choose the complete manual recipient set from the fictional directory. Each selected channel carries a safe directory revision and displayed freshness/on-call evidence."
    >
      <div className="flow-header">
        <p className="version-line">
          Draft version: {draft.draftVersion} · State: {draft.state}
        </p>
        <p>
          Recipient replacement is atomic and creates one new draft version. Any change requires critical-field reconfirmation and exact review again.
        </p>
      </div>

      <form className="recipient-controls" onSubmit={search}>
        <div className="filter-field">
          <label htmlFor="directory-query">Search practitioners</label>
          <input
            id="directory-query"
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder="Name, specialty, or simulation code"
          />
        </div>
        <div className="filter-field">
          <label htmlFor="directory-department">Department filter</label>
          <input
            id="directory-department"
            value={department}
            onChange={(event) => setDepartment(event.target.value)}
            placeholder="Fictional department"
          />
        </div>
        <div className="filter-field">
          <label htmlFor="directory-site">Site filter</label>
          <input
            id="directory-site"
            value={site}
            onChange={(event) => setSite(event.target.value)}
            placeholder="Fictional site"
          />
        </div>
        <div className="filter-field">
          <label htmlFor="directory-on-call">On-call now filter</label>
          <select id="directory-on-call" value={onCallFilter} onChange={(event) => setOnCallFilter(event.target.value)}>
            <option value="any">Any on-call state</option>
            <option value="true">On-call now</option>
          </select>
        </div>
        <button type="submit" disabled={searching}>
          {searching ? "Searching…" : "Search directory"}
        </button>
      </form>

      <p className="selection-summary">Selected recipients ({selectedCount})</p>
      {!canEdit ? <p className="status-message">This draft is not editable. Return to compose to inspect its saved state.</p> : null}

      <form onSubmit={saveRecipientSet}>
        <div className="recipient-list" aria-label="Directory results">
          {results.length === 0 ? <p>No fictional practitioners matched these filters.</p> : null}
          {results.map((practitioner) => (
            <article className="recipient-row" key={practitioner.practitionerId}>
              <div>
                <h2>{practitioner.displayName}</h2>
                <p>
                  {practitioner.roleTitle ?? practitioner.specialty} · {practitioner.department ?? "Department not listed"} · {practitioner.site ?? "Site not listed"}
                </p>
                <p>
                  Directory freshness: {practitioner.isStale ? "stale" : "fresh"} · Last synchronized: {safeUtc(practitioner.lastSynchronizedAtUtc)}
                </p>
                <p>
                  On-call evidence: {practitioner.onCallTier ?? "Not available"} · Last synchronized: {safeUtc(practitioner.onCallLastSynchronizedAtUtc)}
                </p>
                {practitioner.isStale ? (
                  <p className="stale-warning">Stale simulation evidence is selectable only after the operator reviews this warning.</p>
                ) : null}
              </div>
              <div className="channel-options">
                {practitioner.availableChannels.map((channel) => {
                  const key = recipientKey(practitioner.practitionerId, channel);
                  return (
                    <label key={channel}>
                      <input
                        type="checkbox"
                        aria-label={`${practitioner.displayName} — ${channel}`}
                        checked={Boolean(selected[key])}
                        disabled={!canEdit || !practitioner.isActive || !practitioner.selectable}
                        onChange={(event) => toggleRecipient(practitioner, channel, event.target.checked)}
                      />
                      {channel}
                    </label>
                  );
                })}
                {practitioner.availableChannels.length === 0 ? <span>No selectable channel</span> : null}
              </div>
            </article>
          ))}
        </div>
        <div className="form-actions">
          <button type="submit" disabled={!canEdit || saving}>
            {saving ? "Saving recipient set…" : "Save recipient set"}
          </button>
          <Link className="button-secondary" href={`/alerts/${alertId}/compose`}>
            Back to compose
          </Link>
        </div>
      </form>

      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
    </SimulationChrome>
  );
}
