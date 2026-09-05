"use client";
import React from "react";
import type { AlertDraftInput } from "../../lib/alerts";
import { Field } from "./common";

export type DraftContent = Pick<AlertDraftInput, "location" | "urgencyLabel" | "sourceText" | "sbar" | "criticalFields">;
export const emptyContent: DraftContent = { location: "", urgencyLabel: "", sourceText: "", sbar: { situation: "", background: "", assessment: "", recommendation: "" }, criticalFields: [] };
const sbarLabels = { situation: "Situation", background: "Background", assessment: "Assessment", recommendation: "Recommendation" } as const;
export function DraftFields({ value, onChange }: { value: DraftContent; onChange(value: DraftContent): void }) {
  return <>
    <div className="form-grid"><Field label="Fictional location" value={value.location} onChange={location => onChange({ ...value, location })} />
      <Field label="Operator-selected DEMO urgency" value={value.urgencyLabel} onChange={urgencyLabel => onChange({ ...value, urgencyLabel })} /></div>
    <section className="detail-card"><h2>Original Source</h2><p>Enter fictional text beginning with SIMULATION:. Source is saved separately from SBAR and the approved message.</p><Field label="Source text" multiline value={value.sourceText} onChange={sourceText => onChange({ ...value, sourceText })} /></section>
    <section className="detail-card"><h2>Structured SBAR</h2><p>Human-entered simulation content. Begin each section with SIMULATION:.</p>
      {Object.entries(sbarLabels).map(([key, label]) => <Field key={key} label={label} multiline value={value.sbar[key as keyof typeof sbarLabels]} onChange={text => onChange({ ...value, sbar: { ...value.sbar, [key]: text } })} />)}
    </section>
    <section className="detail-card"><h2>Critical values and units</h2><p>Saving a value leaves it unresolved. Confirm each saved value explicitly below.</p>
      {value.criticalFields.map((field, index) => <div className="form-grid" key={index}>
        {([['fieldId', 'identifier'], ['originalValue', 'original value'], ['unit', 'unit']] as const).map(([key, label]) => <Field key={key} label={`Critical field ${index + 1} ${label}`} value={field[key]} required={key !== "unit"} onChange={text => onChange({ ...value, criticalFields: value.criticalFields.map((item, i) => i === index ? { ...item, [key]: text } : item) })} />)}
        <button type="button" className="button-secondary" onClick={() => onChange({ ...value, criticalFields: value.criticalFields.filter((_, i) => i !== index) })}>Remove critical field {index + 1}</button>
      </div>)}
      <button type="button" className="button-secondary" onClick={() => onChange({ ...value, criticalFields: [...value.criticalFields, { fieldId: "", originalValue: "", unit: "" }] })}>Add critical field</button>
    </section>
  </>;
}
