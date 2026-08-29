"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { SimulationChrome } from "../../simulation-chrome";
import { AlertFormFields, AlertFormState, initialAlertForm } from "../alert-form";
import { createAlertDraft, isAlertApiError } from "../../../lib/alerts";

const simulationSiteId = "11111111-1111-4111-8111-111111111201";
const simulationDepartmentId = "11111111-1111-4111-8111-111111110301";

function errorStatus(error: unknown, fallback: string): string {
  if (isAlertApiError(error) && error.status === 401) {
    return "Sign in with a seeded Operator or Administrator identity to draft an alert.";
  }
  if (isAlertApiError(error) && error.status === 403) {
    return "Practitioner identities cannot create or edit alert drafts.";
  }
  return fallback;
}

export default function NewAlertPage() {
  const router = useRouter();
  const [form, setForm] = useState<AlertFormState>(initialAlertForm);
  const [status, setStatus] = useState(
    "Create a typed simulation alert draft. Phase 6 adds manual recipient selection and exact human review.",
  );
  const [saving, setSaving] = useState(false);

  function update(field: keyof AlertFormState, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function createDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    try {
      const draft = await createAlertDraft({
        siteId: simulationSiteId,
        departmentId: simulationDepartmentId,
        simulationPatientReference: form.simulationPatientReference,
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
      setStatus("Draft created. Opening the compose workspace.");
      router.push(`/alerts/${draft.alertId}/compose`);
    } catch (error) {
      setStatus(
        errorStatus(error, "The simulation draft could not be created. Check the required fields and simulation markers."),
      );
    } finally {
      setSaving(false);
    }
  }

  return (
    <SimulationChrome
      title="Create typed simulation alert"
      lead="Create a fictional typed alert, preserve its source and SBAR separately, then continue to manual recipient selection and exact review. No provider action is available in Phase 6."
    >
      <form className="alert-form" onSubmit={createDraft}>
        <AlertFormFields form={form} onChange={update} />
        <div className="form-actions">
          <button type="submit" disabled={saving}>
            {saving ? "Creating draft…" : "Create draft"}
          </button>
        </div>
      </form>
      <p className="status-message" role="status" aria-live="polite">
        {status}
      </p>
    </SimulationChrome>
  );
}
