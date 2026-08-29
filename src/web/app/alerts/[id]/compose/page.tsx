"use client";

import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { SimulationChrome } from "../../../simulation-chrome";
import { AlertFormFields, AlertFormState } from "../../alert-form";
import {
  AlertApiError,
  AlertDraft,
  confirmCriticalField,
  getAlertDraft,
  isAlertApiError,
  setApprovedMessage as saveApprovedMessageApi,
  submitAlertDraft,
  updateAlertDraft,
} from "../../../../lib/alerts";

function routeAlertId(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function formFromDraft(draft: AlertDraft): AlertFormState {
  const criticalField = draft.criticalFields[0];
  return {
    simulationPatientReference: draft.simulationPatientReference,
    location: draft.location,
    urgencyLabel: draft.urgencyLabel,
    sourceText: draft.sourceText ?? "",
    situation: draft.sbar?.situation ?? "",
    background: draft.sbar?.background ?? "",
    assessment: draft.sbar?.assessment ?? "",
    recommendation: draft.sbar?.recommendation ?? "",
    criticalValue: criticalField?.originalValue ?? "",
    criticalUnit: criticalField?.unit ?? "",
    approvedMessage: draft.approvedMessage ?? "",
  };
}

function staleMessage(error: unknown, fallback: string): string {
  if (isAlertApiError(error) && error.status === 409) {
    return "This draft changed elsewhere. Reload it before saving again.";
  }
  if (isAlertApiError(error) && error.status === 401) {
    return "Sign in with a seeded Operator or Administrator identity to continue the simulation review.";
  }
  if (isAlertApiError(error) && error.status === 403) {
    return "Practitioner identities cannot edit or confirm alert drafts.";
  }
  return fallback;
}

function isDraftVersionConflict(error: unknown): error is AlertApiError {
  return isAlertApiError(error) && error.status === 409;
}

export default function AlertComposePage() {
  const params = useParams<{ id: string | string[] }>();
  const alertId = routeAlertId(params.id);
  const [draft, setDraft] = useState<AlertDraft | null>(null);
  const [form, setForm] = useState<AlertFormState | null>(null);
  const [loadedAlertId, setLoadedAlertId] = useState<string | null>(null);
  const [status, setStatus] = useState("Loading the protected simulation draft.");
  const [saving, setSaving] = useState(false);
  const [approvedMessage, setApprovedMessageText] = useState("");

  useEffect(() => {
    let active = true;
    void getAlertDraft(alertId)
      .then((loadedDraft) => {
        if (!active) {
          return;
        }
        setLoadedAlertId(alertId);
        setDraft(loadedDraft);
        setForm(formFromDraft(loadedDraft));
        setApprovedMessageText(loadedDraft.approvedMessage ?? "");
        setStatus("Draft loaded. Review every field before changing it.");
      })
      .catch((error: unknown) => {
        if (!active) {
          return;
        }
        setLoadedAlertId(alertId);
        setStatus(
          staleMessage(error, "The simulation draft could not be loaded. Reload and try again."),
        );
      });

    return () => {
      active = false;
    };
  }, [alertId]);

  function update(field: keyof AlertFormState, value: string) {
    setForm((current) => (current ? { ...current, [field]: value } : current));
  }

  async function saveDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!draft || !form || draft.state !== "Draft") {
      return;
    }
    setSaving(true);
    try {
      const updatedDraft = await updateAlertDraft(alertId, {
        expectedVersion: draft.draftVersion,
        location: form.location,
        urgencyLabel: form.urgencyLabel,
        sourceText: form.sourceText,
        sbar: {
          situation: form.situation,
          background: form.background,
          assessment: form.assessment,
          recommendation: form.recommendation,
        },
        criticalFields: [
          {
            fieldId: "heartRate",
            originalValue: form.criticalValue,
            unit: form.criticalUnit,
          },
        ],
      });
      setDraft(updatedDraft);
      setForm(formFromDraft(updatedDraft));
      setApprovedMessageText(updatedDraft.approvedMessage ?? "");
      setStatus("Draft saved. Critical values remain unresolved until explicitly confirmed.");
    } catch (error) {
      setStatus(
        staleMessage(error, "The simulation draft could not be saved. Check the required fields and simulation markers."),
      );
    } finally {
      setSaving(false);
    }
  }

  async function saveApprovedMessage() {
    if (!draft || draft.state !== "Draft") {
      return;
    }
    setSaving(true);
    try {
      const updatedDraft = await saveApprovedMessageApi(alertId, draft.draftVersion, approvedMessage);
      setDraft(updatedDraft);
      setForm(formFromDraft(updatedDraft));
      setApprovedMessageText(updatedDraft.approvedMessage ?? "");
      setStatus("Approved message saved. Any exact review must use this version.");
    } catch (error) {
      setStatus(
        staleMessage(error, "The approved message could not be saved. Keep the simulation wording explicit and retry."),
      );
    } finally {
      setSaving(false);
    }
  }

  async function confirmField(field: AlertDraft["criticalFields"][number]) {
    if (!draft || field.status === "Confirmed") {
      return;
    }
    setSaving(true);
    try {
      const updatedDraft = await confirmCriticalField(alertId, {
        expectedVersion: draft.draftVersion,
        fieldId: field.fieldId,
        originalValue: field.originalValue,
        normalizedValue: field.normalizedValue,
        unit: field.unit,
      });
      setDraft(updatedDraft);
      setForm(formFromDraft(updatedDraft));
      setApprovedMessageText(updatedDraft.approvedMessage ?? "");
      setStatus("Critical field confirmed by the authenticated simulation user.");
    } catch (error) {
      setStatus(
        staleMessage(error, "The critical field could not be confirmed. Review the value and retry."),
      );
    } finally {
      setSaving(false);
    }
  }

  async function submitForConfirmation() {
    if (!draft || draft.state !== "Draft") {
      return;
    }
    setSaving(true);
    try {
      const pendingDraft = await submitAlertDraft(alertId, draft.draftVersion);
      setDraft(pendingDraft);
      setForm(formFromDraft(pendingDraft));
      setApprovedMessageText(pendingDraft.approvedMessage ?? "");
      setStatus("Draft submitted for exact review. Verify the displayed version and every selected recipient.");
    } catch (error) {
      setStatus(
        isDraftVersionConflict(error)
          ? "This draft changed elsewhere. Reload it before submitting for exact review."
          : "The draft needs complete SBAR content, a recipient set, an approved message, and confirmed critical fields before exact review.",
      );
    } finally {
      setSaving(false);
    }
  }

  if (loadedAlertId !== alertId) {
    return (
      <SimulationChrome
        title="Compose typed simulation alert"
        lead="Loading the protected simulation draft for review."
      >
        <p className="status-message" role="status" aria-live="polite">
          {status}
        </p>
      </SimulationChrome>
    );
  }

  if (!draft || !form) {
    return (
      <SimulationChrome
        title="Compose typed simulation alert"
        lead="The fictional draft could not be loaded for this simulation workspace."
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
  const hasRecipients = draft.recipients.length > 0;

  return (
    <SimulationChrome
      title="Compose typed simulation alert"
      lead="Edit the fictional source and SBAR, confirm every critical value, write the approved message, and select recipients before exact human review."
    >
      <div className="flow-header">
        <p className="version-line">
          Draft version: {draft.draftVersion} · State: {draft.state}
        </p>
        <p>
          Source type: {draft.sourceType}. Original typed source, SBAR, approved message, and recipient evidence remain separate records.
        </p>
      </div>

      <form className="alert-form" onSubmit={saveDraft}>
        <fieldset disabled={!canEdit || saving}>
          <AlertFormFields form={form} onChange={update} patientReadOnly />
          <div className="form-actions">
            <button type="submit">{saving ? "Saving…" : "Save draft"}</button>
          </div>
        </fieldset>
      </form>

      <section className="alert-panel" aria-labelledby="approved-message-heading">
        <h2 id="approved-message-heading">Approved message record</h2>
        <p>Write the exact human-approved message separately from the typed source and SBAR.</p>
        <label htmlFor="approved-message">Approved message</label>
        <textarea
          id="approved-message"
          value={approvedMessage}
          onChange={(event) => setApprovedMessageText(event.target.value)}
          disabled={!canEdit || saving}
          required
        />
        <div className="form-actions">
          <button type="button" onClick={() => void saveApprovedMessage()} disabled={!canEdit || saving}>
            Save approved message
          </button>
        </div>
      </section>

      <section className="alert-panel" aria-labelledby="recipient-heading">
        <h2 id="recipient-heading">Manual recipient set</h2>
        <p>
          {hasRecipients
            ? `${draft.recipients.length} fictional recipient channel${draft.recipients.length === 1 ? "" : "s"} selected.`
            : "No recipients selected. Choose a complete set from the fictional directory before exact review."}
        </p>
        {canEdit ? (
          <Link className="focus-link" href={`/alerts/${alertId}/recipients`}>
            Select recipients
          </Link>
        ) : null}
      </section>

      <section className="alert-panel" aria-labelledby="critical-heading">
        <h2 id="critical-heading">Critical-field confirmation</h2>
        <p>Every number and unit remains unresolved until an authenticated human confirms the exact value.</p>
        {draft.criticalFields.length === 0 ? <p>No critical fields were recorded.</p> : null}
        <div className="review-grid">
          {draft.criticalFields.map((field) => (
            <div className="review-item" key={`${field.alertVersion}-${field.fieldId}`}>
              <strong>{field.fieldId}</strong>
              <span>
                {field.normalizedValue} {field.unit ?? ""}
              </span>
              <span>Status: {field.status}</span>
              {field.status !== "Confirmed" ? (
                <button type="button" onClick={() => void confirmField(field)} disabled={!canEdit || saving}>
                  Confirm {field.fieldId}
                </button>
              ) : null}
            </div>
          ))}
        </div>
        <div className="form-actions">
          <button type="button" onClick={() => void submitForConfirmation()} disabled={!canEdit || saving}>
            Submit for confirmation
          </button>
        </div>
        {!canEdit ? (
          <p>
            <Link className="focus-link" href={`/alerts/${alertId}/review`}>
              Open exact review
            </Link>
          </p>
        ) : null}
      </section>

      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
    </SimulationChrome>
  );
}
