"use client";

import React, { ChangeEvent, FormEvent, useState } from "react";
import { SimulationChrome } from "../../simulation-chrome";

const simulationSiteId = "11111111-1111-4111-8111-111111111201";
const simulationDepartmentId = "11111111-1111-4111-8111-111111110301";

type AlertField = {
  fieldId: string;
  originalValue: string;
  normalizedValue: string;
  unit: string | null;
  status: string;
};

type AlertDraft = {
  alertId: string;
  state: string;
  draftVersion: number;
  simulationPatientReference: string;
  location: string;
  urgencyLabel: string;
  sourceType: string;
  sourceText: string | null;
  sbar: {
    situation: string;
    background: string;
    assessment: string;
    recommendation: string;
  } | null;
  criticalFields: AlertField[];
};

type FormState = {
  simulationPatientReference: string;
  location: string;
  urgencyLabel: string;
  sourceText: string;
  situation: string;
  background: string;
  assessment: string;
  recommendation: string;
  criticalValue: string;
  criticalUnit: string;
};

const initialForm: FormState = {
  simulationPatientReference: "SIM-PAT-0001",
  location: "North Wing / Simulation Room 204",
  urgencyLabel: "Urgent",
  sourceText: "SIMULATION: fictional typed source",
  situation: "SIMULATION: fictional situation",
  background: "SIMULATION: fictional background",
  assessment: "SIMULATION: fictional assessment",
  recommendation: "SIMULATION: fictional recommendation",
  criticalValue: "118",
  criticalUnit: "beats/min",
};

export default function AlertComposePage() {
  const [form, setForm] = useState<FormState>(initialForm);
  const [draft, setDraft] = useState<AlertDraft | null>(null);
  const [status, setStatus] = useState("Create a typed simulation alert draft. Nothing is dispatched from this page.");

  function update(field: keyof FormState) {
    return (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      setForm((current) => ({ ...current, [field]: event.target.value }));
    };
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    const payload = draft
      ? {
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
        }
      : {
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
        };
    const path = draft ? `/api/alerts/${draft.alertId}` : "/api/alerts/drafts";
    const response = await fetch(path, {
      method: draft ? "PATCH" : "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify(payload),
    });
    if (response.status === 401) {
      setStatus("Sign in with a seeded Operator or Administrator identity to draft an alert.");
      return;
    }
    if (response.status === 403) {
      setStatus("Practitioner identities cannot create or edit alert drafts.");
      return;
    }
    if (response.status === 409) {
      setStatus("This draft changed elsewhere. Reload it before saving again.");
      return;
    }
    if (!response.ok) {
      setStatus("The simulation draft could not be saved. Check the required fields and simulation markers.");
      return;
    }

    const loaded = (await response.json()) as AlertDraft;
    setDraft(loaded);
    setStatus("Draft saved. Critical values remain unresolved until explicitly confirmed.");
  }

  async function confirmField(field: AlertField) {
    if (!draft) {
      return;
    }

    const response = await fetch(`/api/alerts/${draft.alertId}/field-confirmations`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({
        expectedVersion: draft.draftVersion,
        fieldId: field.fieldId,
        originalValue: field.originalValue,
        normalizedValue: field.normalizedValue,
        unit: field.unit,
      }),
    });
    if (response.status === 409) {
      setStatus("This draft changed elsewhere. Reload it before confirming the field.");
      return;
    }
    if (!response.ok) {
      setStatus("The critical field could not be confirmed.");
      return;
    }

    setDraft((await response.json()) as AlertDraft);
    setStatus("Critical field confirmed by the authenticated simulation user.");
  }

  async function submitForConfirmation() {
    if (!draft) {
      return;
    }

    const response = await fetch(`/api/alerts/${draft.alertId}/submit-for-confirmation`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      credentials: "include",
      body: JSON.stringify({ expectedVersion: draft.draftVersion }),
    });
    if (response.status === 409) {
      setStatus("This draft changed elsewhere. Reload it before submitting for confirmation.");
      return;
    }
    if (!response.ok) {
      setStatus("The draft needs complete SBAR content and confirmed critical fields before submission.");
      return;
    }

    setDraft((await response.json()) as AlertDraft);
    setStatus("Draft submitted for confirmation. Recipient selection and dispatch are not available in this phase.");
  }

  return (
    <SimulationChrome
      title="Typed alert drafting"
      lead="Phase 5 supports a fictional typed source, SBAR content, critical-field confirmation, and optimistic draft versions. Recipient selection and dispatch remain unavailable."
    >
      <form className="directory-search" onSubmit={save}>
        <label htmlFor="alert-patient">Synthetic patient reference</label>
        <input id="alert-patient" value={form.simulationPatientReference} onChange={update("simulationPatientReference")} required />
        <label htmlFor="alert-location">Simulation location</label>
        <input id="alert-location" value={form.location} onChange={update("location")} required />
        <label htmlFor="alert-urgency">Urgency label</label>
        <input id="alert-urgency" value={form.urgencyLabel} onChange={update("urgencyLabel")} required />
        <label htmlFor="alert-source">Typed source</label>
        <textarea id="alert-source" value={form.sourceText} onChange={update("sourceText")} required />
        <label htmlFor="alert-situation">Situation</label>
        <textarea id="alert-situation" value={form.situation} onChange={update("situation")} required />
        <label htmlFor="alert-background">Background</label>
        <textarea id="alert-background" value={form.background} onChange={update("background")} required />
        <label htmlFor="alert-assessment">Assessment</label>
        <textarea id="alert-assessment" value={form.assessment} onChange={update("assessment")} required />
        <label htmlFor="alert-recommendation">Recommendation</label>
        <textarea id="alert-recommendation" value={form.recommendation} onChange={update("recommendation")} required />
        {!draft ? (
          <>
            <label htmlFor="critical-value">Critical value (simulation)</label>
            <input id="critical-value" value={form.criticalValue} onChange={update("criticalValue")} required />
            <label htmlFor="critical-unit">Critical value unit</label>
            <input id="critical-unit" value={form.criticalUnit} onChange={update("criticalUnit")} required />
          </>
        ) : null}
        <button type="submit">{draft ? "Save draft" : "Create draft"}</button>
      </form>
      <p role="status">{status}</p>
      {draft ? (
        <section aria-label="Draft status">
          <p>
            Draft version: {draft.draftVersion} · State: {draft.state} · Source type: {draft.sourceType}
          </p>
          <h2>Critical-field confirmation</h2>
          {draft.criticalFields.length === 0 ? <p>No critical fields were recorded.</p> : null}
          {draft.criticalFields.map((field) => (
            <div key={field.fieldId}>
              <p>
                {field.fieldId}: {field.normalizedValue} {field.unit ?? ""} ({field.status})
              </p>
              {field.status !== "Confirmed" ? (
                <button type="button" onClick={() => void confirmField(field)}>
                  Confirm {field.fieldId}
                </button>
              ) : null}
            </div>
          ))}
          <button type="button" onClick={() => void submitForConfirmation()} disabled={draft.state !== "Draft"}>
            Submit for confirmation
          </button>
          <p>Recipient selection and dispatch are intentionally unavailable in Phase 5.</p>
        </section>
      ) : null}
    </SimulationChrome>
  );
}
