"use client";

import { FormEvent, useState } from "react";
import { PageHeader } from "../../../components/ui/page-header";
import { usePrototype } from "../../../features/alerts/prototype-store";
import type { Urgency } from "../../../features/alerts/types";
import { AlertFormFields, AlertFormState, initialAlertForm } from "../alert-form";

function urgencyFromLabel(label: string): Urgency {
  const normalized = label.toLowerCase();
  if (normalized.includes("critical")) return "critical";
  if (normalized.includes("urgent") || normalized.includes("high")) return "high";
  return "routine";
}

function caseDetailsFromForm(form: AlertFormState) {
  return [
    form.sourceText,
    form.situation,
    form.background,
    form.assessment,
    form.recommendation,
    `SIMULATION: fictional critical value ${form.criticalValue} ${form.criticalUnit}.`,
  ]
    .filter(Boolean)
    .join("\n\n");
}

export default function NewAlertPage() {
  const { createAlert, state } = usePrototype();
  const [form, setForm] = useState<AlertFormState>(initialAlertForm);
  const [status, setStatus] = useState("Create a fictional local alert draft. No provider or backend is contacted.");
  const [createdAlertId, setCreatedAlertId] = useState<string | null>(null);

  function update(field: keyof AlertFormState, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  function createDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const alertId = createAlert({
      patientReference: form.simulationPatientReference,
      location: form.location,
      department: "Fictional Emergency",
      urgency: urgencyFromLabel(form.urgencyLabel),
      caseDetails: caseDetailsFromForm(form),
      clinicianIds: state.clinicians.slice(0, 1).map((clinician) => clinician.id),
    });
    setStatus("Draft created in local prototype state");
    setCreatedAlertId(alertId);
  }

  function createAnotherDraft() {
    setForm(initialAlertForm);
    setCreatedAlertId(null);
    setStatus("Create a fictional local alert draft. No provider or backend is contacted.");
  }

  return (
    <>
      <PageHeader
        title="Create typed simulation alert"
        description="Create a fictional typed alert in local prototype state. No provider action is available."
      />
      <form className="alert-form" onSubmit={createDraft}>
        <AlertFormFields form={form} onChange={update} />
        <div className="form-actions">
          <button type="submit">Create draft</button>
        </div>
      </form>
      <p className="status-message" role="status" aria-live="polite" aria-label={status}>
        {status}
      </p>
      {createdAlertId ? (
        <section className="alert-panel" aria-labelledby="local-draft-created-heading">
          <h2 id="local-draft-created-heading">Draft created</h2>
          <p>
            Local draft {createdAlertId} is saved in this browser's fictional prototype state. Review and confirmation
            screens are coming in the next prototype task.
          </p>
          <div className="form-actions">
            <button type="button" onClick={createAnotherDraft}>
              Create another draft
            </button>
            <button type="button" title="Coming later" disabled>
              Review & Confirm — Coming later
            </button>
          </div>
        </section>
      ) : null}
    </>
  );
}
