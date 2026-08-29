"use client";

import type { ChangeEvent } from "react";

export type AlertFormState = {
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
  approvedMessage: string;
};

export const initialAlertForm: AlertFormState = {
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
  approvedMessage: "",
};

type AlertFormFieldsProps = {
  form: AlertFormState;
  onChange: (field: keyof AlertFormState, value: string) => void;
  patientReadOnly?: boolean;
};

export function AlertFormFields({ form, onChange, patientReadOnly = false }: AlertFormFieldsProps) {
  function handleChange(field: keyof AlertFormState) {
    return (event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => onChange(field, event.target.value);
  }

  return (
    <div className="form-grid">
      <div className="form-section">
        <h2>Alert context</h2>
        <label htmlFor="alert-patient">Synthetic patient reference</label>
        <input
          id="alert-patient"
          value={form.simulationPatientReference}
          onChange={handleChange("simulationPatientReference")}
          readOnly={patientReadOnly}
          required
        />
        <label htmlFor="alert-location">Simulation location</label>
        <input id="alert-location" value={form.location} onChange={handleChange("location")} required />
        <label htmlFor="alert-urgency">Urgency label</label>
        <input id="alert-urgency" value={form.urgencyLabel} onChange={handleChange("urgencyLabel")} required />
      </div>

      <div className="form-section">
        <h2>Source and SBAR</h2>
        <label htmlFor="alert-source">Typed source</label>
        <textarea id="alert-source" value={form.sourceText} onChange={handleChange("sourceText")} required />
        <label htmlFor="alert-situation">Situation</label>
        <textarea id="alert-situation" value={form.situation} onChange={handleChange("situation")} required />
        <label htmlFor="alert-background">Background</label>
        <textarea id="alert-background" value={form.background} onChange={handleChange("background")} required />
        <label htmlFor="alert-assessment">Assessment</label>
        <textarea id="alert-assessment" value={form.assessment} onChange={handleChange("assessment")} required />
        <label htmlFor="alert-recommendation">Recommendation</label>
        <textarea
          id="alert-recommendation"
          value={form.recommendation}
          onChange={handleChange("recommendation")}
          required
        />
      </div>

      <div className="form-section">
        <h2>Critical value</h2>
        <p>Every number and unit remains unresolved until an authenticated human confirms the exact value.</p>
        <label htmlFor="critical-value">Critical value (simulation)</label>
        <input id="critical-value" value={form.criticalValue} onChange={handleChange("criticalValue")} required />
        <label htmlFor="critical-unit">Critical value unit</label>
        <input id="critical-unit" value={form.criticalUnit} onChange={handleChange("criticalUnit")} required />
      </div>
    </div>
  );
}
