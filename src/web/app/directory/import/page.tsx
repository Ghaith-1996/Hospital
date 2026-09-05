"use client";

import React, { ChangeEvent, FormEvent, useState } from "react";
import { SimulationChrome } from "../../simulation-chrome";

type DirectoryImportIssue = {
  code: string;
  sourceRecordId: string;
  rowNumber: number | null;
  message: string;
};

type DirectoryImportChange = {
  action: string;
  sourceRecordId: string;
  simulationCode: string;
  displayName: string;
  selectable: boolean;
};

type DirectoryImportPreview = {
  sourceSystem: string;
  parsedPractitionerCount: number;
  insertCount: number;
  updateCount: number;
  rejectedCount: number;
  errors: DirectoryImportIssue[];
  warnings: DirectoryImportIssue[];
  changes: DirectoryImportChange[];
  previewToken: string;
};

type DirectoryImportApply = {
  applied: boolean;
  syncRunId: string | null;
  preview: DirectoryImportPreview;
};

export default function DirectoryImportPage() {
  const [file, setFile] = useState<File | null>(null);
  const [status, setStatus] = useState(
    "Administrator only. Preview validates the fictional CSV adapter without writing. Production directory sources remain REQUIRES_HOSPITAL_DECISION.",
  );
  const [preview, setPreview] = useState<DirectoryImportPreview | null>(null);

  async function post(path: string) {
    if (!file) {
      setStatus("Choose the fictional Harborview CSV before previewing or applying.");
      return;
    }

    const body = new FormData();
    body.append("file", file);
    if (!path.endsWith("/preview")) {
      body.append("preview_token", preview?.previewToken ?? "");
    }
    const response = await fetch(path, { method: "POST", body, credentials: "include" });
    if (response.status === 401) {
      setStatus("Sign in with the seeded Administrator identity to import.");
      return;
    }
    if (response.status === 403) {
      setStatus("Only the seeded Administrator role may import the directory.");
      return;
    }

    if (path.endsWith("/preview")) {
      if (!response.ok) {
        setStatus("The CSV preview could not be completed.");
        return;
      }
      const loaded = (await response.json()) as DirectoryImportPreview;
      setPreview(loaded);
      setStatus(
        loaded.errors.length > 0
          ? "Preview found blocking conflicts. Nothing was written."
          : `Preview ready for ${loaded.sourceSystem}. Nothing was written.`,
      );
      return;
    }

    const loaded = (await response.json()) as DirectoryImportApply;
    setPreview(loaded.preview);
    setStatus(
      loaded.applied
        ? `Applied ${loaded.preview.sourceSystem}. CSV remains an adapter, not the directory model.`
        : "Apply was rejected. Nothing was written for blocking conflicts.",
    );
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void post("/api/v1/directory/imports/preview");
  }

  function onFileChange(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0] ?? null);
    setPreview(null);
    setStatus("Choose a new preview before applying the selected CSV.");
  }

  return (
    <SimulationChrome
      title="Fictional directory import"
      lead="CSV is the first directory adapter. Later SCIM, Graph, FHIR, scheduling, and restricted SQL views must normalize into the same practitioner model."
    >
      <form className="directory-search" onSubmit={onSubmit}>
        <label htmlFor="directory-csv">Simulation CSV</label>
        <input
          id="directory-csv"
          name="file"
          type="file"
          accept=".csv,text/csv"
          onChange={onFileChange}
        />
        <button type="submit">Preview import</button>
        <button
          type="button"
          disabled={!preview || preview.errors.length > 0}
          onClick={() => void post("/api/v1/directory/imports")}
        >
          Apply import
        </button>
      </form>
      <p>{status}</p>
      {preview ? (
        <div>
          <p>
            {preview.sourceSystem}: {preview.insertCount} insert(s), {preview.updateCount} update(s),{" "}
            {preview.rejectedCount} rejected.
          </p>
          {preview.warnings.length > 0 ? (
            <ul aria-label="Import warnings">
              {preview.warnings.map((warning) => (
                <li key={`${warning.code}-${warning.sourceRecordId}`}>{warning.message}</li>
              ))}
            </ul>
          ) : null}
          {preview.errors.length > 0 ? (
            <ul aria-label="Import errors">
              {preview.errors.map((error) => (
                <li key={`${error.code}-${error.sourceRecordId}`}>{error.message}</li>
              ))}
            </ul>
          ) : null}
          <div className="table-wrap">
            <table className="directory-table">
              <thead>
                <tr>
                  <th scope="col">Action</th>
                  <th scope="col">Name</th>
                  <th scope="col">Source ID</th>
                  <th scope="col">Synthetic ID</th>
                  <th scope="col">Selectable</th>
                </tr>
              </thead>
              <tbody>
                {preview.changes.map((change) => (
                  <tr key={change.sourceRecordId}>
                    <td>{change.action}</td>
                    <td>{change.displayName}</td>
                    <td>{change.sourceRecordId}</td>
                    <td>{change.simulationCode}</td>
                    <td>{change.selectable ? "Yes" : "No"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </SimulationChrome>
  );
}
