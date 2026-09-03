"use client";

import React, { FormEvent, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { AlertSummary } from "../../../components/alerts/alert-summary";
import { ClinicianSelector } from "../../../components/alerts/clinician-selector";
import { PageHeader } from "../../../components/ui/page-header";
import { searchClinicians, selectAlertById } from "../../../features/alerts/selectors";
import { usePrototype } from "../../../features/alerts/prototype-store";
import type { Clinician, NewAlertInput, Urgency } from "../../../features/alerts/types";

const MAX_CASE_DETAILS_LENGTH = 4000;
const DEFAULT_LOCATION = "Fictional ER - Simulation Bed 12";
const DEFAULT_DEPARTMENT = "Fictional Emergency";

type EntryMode = "type" | "dictate";

type ValidationErrors = {
  patientReference?: string;
  urgency?: string;
  caseDetails?: string;
  clinicians?: string;
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

export default function NewAlertPage() {
  const router = useRouter();
  const { createAlert, hydrated, state, updateAlert } = usePrototype();
  const [patientReference, setPatientReference] = useState("");
  const [urgency, setUrgency] = useState<Urgency>("critical");
  const [caseDetails, setCaseDetails] = useState("");
  const [entryMode, setEntryMode] = useState<EntryMode>("type");
  const [clinicianQuery, setClinicianQuery] = useState("");
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [loadedEditId, setLoadedEditId] = useState<string | null>(null);

  useEffect(() => {
    if (!hydrated || typeof window === "undefined") return;

    const queryEditId = new URLSearchParams(window.location.search).get("edit");
    if (!queryEditId || queryEditId === loadedEditId) return;

    const alert = selectAlertById(state, queryEditId);
    if (!alert) return;

    setEditId(queryEditId);
    setLoadedEditId(queryEditId);
    setPatientReference(alert.patientReference);
    setUrgency(alert.urgency);
    setCaseDetails(alert.caseDetails.slice(0, MAX_CASE_DETAILS_LENGTH));
    setSelectedIds(alert.recipients.map((recipient) => recipient.clinicianId));
    setSubmitted(false);
    setClinicianQuery("");
    setEntryMode("type");
  }, [hydrated, loadedEditId, state]);

  const errors = submitted ? buildValidationErrors(patientReference, urgency, caseDetails, selectedIds) : {};
  const clinicianOptions = useMemo(() => searchClinicians(state, ""), [state]);
  const selected = useMemo(() => selectedClinicians(clinicianOptions, selectedIds), [clinicianOptions, selectedIds]);

  function updateCaseDetails(value: string) {
    setCaseDetails(value.slice(0, MAX_CASE_DETAILS_LENGTH));
  }

  function addClinician(id: string) {
    setSelectedIds((current) => (current.includes(id) ? current : [...current, id]));
  }

  function removeClinician(id: string) {
    setSelectedIds((current) => current.filter((selectedId) => selectedId !== id));
  }

  function clearForm() {
    setPatientReference("");
    setUrgency("critical");
    setCaseDetails("");
    setEntryMode("type");
    setClinicianQuery("");
    setSelectedIds([]);
    setSubmitted(false);
    setEditId(null);
    setLoadedEditId(null);
  }

  function buildInput(): NewAlertInput {
    return {
      patientReference: patientReference.trim(),
      location: DEFAULT_LOCATION,
      department: DEFAULT_DEPARTMENT,
      urgency,
      caseDetails: caseDetails.trim(),
      clinicianIds: selectedIds,
    };
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitted(true);

    const nextErrors = buildValidationErrors(patientReference, urgency, caseDetails, selectedIds);
    if (Object.keys(nextErrors).length > 0) return;

    const input = buildInput();
    const existingEditId = editId && selectAlertById(state, editId) ? editId : null;
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
              value={patientReference}
              onChange={(event) => setPatientReference(event.target.value)}
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
              value={urgency}
              onChange={(event) => setUrgency(event.target.value as Urgency)}
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
            <button type="button" aria-pressed={entryMode === "type"} onClick={() => setEntryMode("type")}>
              Type
            </button>
            <button type="button" aria-pressed={entryMode === "dictate"} onClick={() => setEntryMode("dictate")}>
              Dictate
            </button>
          </div>

          {entryMode === "type" ? (
            <label className="filter-field" htmlFor="case-details">
              Case Details
              <textarea
                id="case-details"
                value={caseDetails}
                onChange={(event) => updateCaseDetails(event.target.value)}
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
            {caseDetails.length}/4000 characters
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
            selectedIds={selectedIds}
            query={clinicianQuery}
            onQueryChange={setClinicianQuery}
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
          patientReference={patientReference}
          urgency={urgency}
          caseDetails={caseDetails}
          selectedClinicians={selected}
        />
      </form>
    </>
  );
}
