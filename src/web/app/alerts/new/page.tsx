"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
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
  const router = useRouter();
  const { createAlert, state } = usePrototype();
  const [form, setForm] = useState<AlertFormState>(initialAlertForm);
  const [status, setStatus] = useState("Create a fictional local alert draft. No provider or backend is contacted.");

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
    setStatus("Draft created in local prototype state.");
    router.push(`/alerts/${alertId}/compose`);
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
      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
    </>
  );
}
