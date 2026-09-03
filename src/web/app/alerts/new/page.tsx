"use client";

import React, { FormEvent, useEffect, useMemo, useReducer } from "react";
import { useRouter } from "next/navigation";
import { AlertSummary } from "../../../components/alerts/alert-summary";
import { ClinicianSelector } from "../../../components/alerts/clinician-selector";
import { PageHeader } from "../../../components/ui/page-header";
import { searchClinicians, selectAlertById } from "../../../features/alerts/selectors";
import { usePrototype } from "../../../features/alerts/prototype-store";
import type { AlertRecord, Clinician, NewAlertInput, Urgency } from "../../../features/alerts/types";

const MAX_CASE_DETAILS_LENGTH = 4000;
const DEFAULT_LOCATION = "Fictional ER - Simulation Bed 12";
const DEFAULT_DEPARTMENT = "Fictional Emergency";

type EntryMode = "type" | "dictate";

type FormState = {
  patientReference: string;
  urgency: Urgency;
  caseDetails: string;
  entryMode: EntryMode;
  clinicianQuery: string;
  selectedIds: string[];
  submitted: boolean;
  editId: string | null;
  loadedEditId: string | null;
};

type ValidationErrors = {
  patientReference?: string;
  urgency?: string;
  caseDetails?: string;
  clinicians?: string;
};

type FormAction =
  | { type: "patient-reference-changed"; value: string }
  | { type: "urgency-changed"; value: Urgency }
  | { type: "case-details-changed"; value: string }
  | { type: "entry-mode-changed"; value: EntryMode }
  | { type: "clinician-query-changed"; value: string }
  | { type: "clinician-added"; id: string }
  | { type: "clinician-removed"; id: string }
  | { type: "submitted" }
  | { type: "cleared"; queryEditId: string | null }
  | { type: "edit-loaded"; alert: AlertRecord };

const initialFormState: FormState = {
  patientReference: "",
  urgency: "critical",
  caseDetails: "",
  entryMode: "type",
  clinicianQuery: "",
  selectedIds: [],
  submitted: false,
  editId: null,
  loadedEditId: null,
};

function selectedClinicians(clinicians: Clinician[], selectedIds: string[]) {
  return selectedIds
    .map((id) => clinicians.find((clinician) => clinician.id === id))
    .filter((clinician): clinician is Clinician => Boolean(clinician));
}

function buildValidationErrors(patientReference: string, urgency: Urgency | "", caseDetails: string, selectedIds: string[]) {
  const errors: ValidationErrors = {};
  if (!patientReference.trim()) errors.patientReference = "Patient reference is required.";
  if (!urgency) errors.urgency = "Urgency level is required.";
  if (!caseDetails.trim()) errors.caseDetails = "Case details are required.";
  if (selectedIds.length === 0) errors.clinicians = "Select at least one fictional clinician.";
  return errors;
}

function formReducer(state: FormState, action: FormAction): FormState {
  if (action.type === "patient-reference-changed") return { ...state, patientReference: action.value };
  if (action.type === "urgency-changed") return { ...state, urgency: action.value };
  if (action.type === "case-details-changed") {
    return { ...state, caseDetails: action.value.slice(0, MAX_CASE_DETAILS_LENGTH) };
  }
  if (action.type === "entry-mode-changed") return { ...state, entryMode: action.value };
  if (action.type === "clinician-query-changed") return { ...state, clinicianQuery: action.value };
  if (action.type === "clinician-added") {
    return state.selectedIds.includes(action.id)
      ? state
      : { ...state, selectedIds: [...state.selectedIds, action.id] };
  }
  if (action.type === "clinician-removed") {
    return { ...state, selectedIds: state.selectedIds.filter((selectedId) => selectedId !== action.id) };
  }
  if (action.type === "submitted") return { ...state, submitted: true };
  if (action.type === "cleared") return { ...initialFormState, loadedEditId: action.queryEditId };
  return {
    ...initialFormState,
    patientReference: action.alert.patientReference,
    urgency: action.alert.urgency,
    caseDetails: action.alert.caseDetails.slice(0, MAX_CASE_DETAILS_LENGTH),
    selectedIds: action.alert.recipients.map((recipient) => recipient.clinicianId),
    editId: action.alert.id,
    loadedEditId: action.alert.id,
  };
}

export default function NewAlertPage() {
  const router = useRouter();
  const { createAlert, hydrated, resetGeneration, state, updateAlert } = usePrototype();
  const [form, dispatch] = useReducer(formReducer, initialFormState);
  const previousResetGeneration = React.useRef(resetGeneration);

  useEffect(() => {
    if (!hydrated || typeof window === "undefined") return;

    const queryEditId = new URLSearchParams(window.location.search).get("edit");
    if (!queryEditId || queryEditId === form.loadedEditId) return;

    const alert = selectAlertById(state, queryEditId);
    if (!alert) return;

    dispatch({ type: "edit-loaded", alert });
  }, [form.loadedEditId, hydrated, state]);

  useEffect(() => {
    if (previousResetGeneration.current === resetGeneration) return;
    previousResetGeneration.current = resetGeneration;

    const queryEditId = typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get("edit");
    dispatch({ type: "cleared", queryEditId });
    if (queryEditId) {
      router.replace("/alerts/new");
    }
  }, [resetGeneration, router]);

  const errors = form.submitted
    ? buildValidationErrors(form.patientReference, form.urgency, form.caseDetails, form.selectedIds)
    : {};
  const clinicianOptions = useMemo(() => searchClinicians(state, ""), [state]);
  const selected = useMemo(
    () => selectedClinicians(clinicianOptions, form.selectedIds),
    [clinicianOptions, form.selectedIds],
  );

  function addClinician(id: string) {
    dispatch({ type: "clinician-added", id });
  }

  function removeClinician(id: string) {
    dispatch({ type: "clinician-removed", id });
  }

  function clearForm() {
    const queryEditId = typeof window === "undefined" ? null : new URLSearchParams(window.location.search).get("edit");
    dispatch({ type: "cleared", queryEditId });
    if (queryEditId) {
      router.replace("/alerts/new");
    }
  }

  function buildInput(): NewAlertInput {
    return {
      patientReference: form.patientReference.trim(),
      location: DEFAULT_LOCATION,
      department: DEFAULT_DEPARTMENT,
      urgency: form.urgency,
      caseDetails: form.caseDetails.trim(),
      clinicianIds: form.selectedIds,
    };
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    dispatch({ type: "submitted" });

    const nextErrors = buildValidationErrors(form.patientReference, form.urgency, form.caseDetails, form.selectedIds);
    if (Object.keys(nextErrors).length > 0) return;

    const input = buildInput();
    const existingEditId = form.editId && selectAlertById(state, form.editId) ? form.editId : null;
    const alertId = existingEditId ?? createAlert(input);

    if (existingEditId) {
      updateAlert(existingEditId, input);
    }

    router.push(`/alerts/${alertId}/review`);
  }

  return (
    <>
      <PageHeader title="Alert Doctor" description="Create a new alert and notify the right clinician, fast." />
      <form className="new-alert-layout" onSubmit={handleSubmit} noValidate>
        <section className="new-alert-form" aria-labelledby="new-alert-heading">
          <div className="section-heading">
            <h2 id="new-alert-heading">New Alert</h2>
            <span className="simulation-pill">SIMULATION ONLY</span>
          </div>

          <label className="filter-field" htmlFor="patient-reference">
            Patient Reference
            <input
              id="patient-reference"
              value={form.patientReference}
              onChange={(event) => dispatch({ type: "patient-reference-changed", value: event.target.value })}
              aria-describedby={errors.patientReference ? "patient-reference-error" : undefined}
            />
          </label>
          {errors.patientReference ? (
            <p className="field-error" id="patient-reference-error">
              {errors.patientReference}
            </p>
          ) : null}

          <label className="filter-field" htmlFor="urgency-level">
            Urgency Level
            <select
              id="urgency-level"
              value={form.urgency}
              onChange={(event) => dispatch({ type: "urgency-changed", value: event.target.value as Urgency })}
              aria-describedby={errors.urgency ? "urgency-level-error" : undefined}
            >
              <option value="critical">Critical</option>
              <option value="high">High</option>
              <option value="routine">Routine</option>
            </select>
          </label>
          {errors.urgency ? (
            <p className="field-error" id="urgency-level-error">
              {errors.urgency}
            </p>
          ) : null}

          <div className="entry-mode" aria-label="Case detail entry mode">
            <button
              type="button"
              aria-pressed={form.entryMode === "type"}
              onClick={() => dispatch({ type: "entry-mode-changed", value: "type" })}
            >
              Type
            </button>
            <button
              type="button"
              aria-pressed={form.entryMode === "dictate"}
              onClick={() => dispatch({ type: "entry-mode-changed", value: "dictate" })}
            >
              Dictate
            </button>
          </div>

          {form.entryMode === "type" ? (
            <label className="filter-field" htmlFor="case-details">
              Case Details
              <textarea
                id="case-details"
                value={form.caseDetails}
                onChange={(event) => dispatch({ type: "case-details-changed", value: event.target.value })}
                rows={9}
                aria-describedby={errors.caseDetails ? "case-details-error case-details-counter" : "case-details-counter"}
              />
            </label>
          ) : (
            <section className="dictation-panel" aria-labelledby="dictation-heading">
              <h3 id="dictation-heading">Dictation unavailable</h3>
              <p>Dictation is not connected in this frontend prototype. Type the fictional case details instead.</p>
            </section>
          )}
          <p className="character-counter" id="case-details-counter">
            {form.caseDetails.length}/4000 characters
          </p>
          {errors.caseDetails ? (
            <p className="field-error" id="case-details-error">
              {errors.caseDetails}
            </p>
          ) : null}

          <p className="simulation-notice">
            Simulation only: use fictional patient references and fictional case details. This local prototype does not
            contact clinicians, observe delivery, use a backend, or record audio.
          </p>

          <ClinicianSelector
            clinicians={clinicianOptions}
            selectedIds={form.selectedIds}
            query={form.clinicianQuery}
            onQueryChange={(query) => dispatch({ type: "clinician-query-changed", value: query })}
            onAdd={addClinician}
            onRemove={removeClinician}
            error={errors.clinicians}
          />

          <div className="form-actions new-alert-actions">
            <button type="button" className="button-secondary" onClick={clearForm}>
              Clear
            </button>
            <button type="submit">Review &amp; Confirm</button>
          </div>
        </section>

        <AlertSummary
          patientReference={form.patientReference}
          urgency={form.urgency}
          caseDetails={form.caseDetails}
          selectedClinicians={selected}
        />
      </form>
    </>
  );
}
