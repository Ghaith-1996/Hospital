import React from "react";
import type { Clinician } from "../../features/alerts/types";

export type ClinicianSelectorProps = {
  clinicians: Clinician[];
  selectedIds: string[];
  query: string;
  onQueryChange(query: string): void;
  onAdd(id: string): void;
  onRemove(id: string): void;
  error?: string;
};

function matchesClinician(clinician: Clinician, query: string) {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return true;

  return [clinician.displayName, clinician.specialty, clinician.department].some((value) =>
    value.toLowerCase().includes(normalized),
  );
}

export function ClinicianSelector({
  clinicians,
  selectedIds,
  query,
  onQueryChange,
  onAdd,
  onRemove,
  error,
}: ClinicianSelectorProps) {
  const selectedClinicians = selectedIds
    .map((id) => clinicians.find((clinician) => clinician.id === id))
    .filter((clinician): clinician is Clinician => Boolean(clinician));
  const selectedIdSet = new Set(selectedIds);
  const filteredClinicians = clinicians.filter((clinician) => matchesClinician(clinician, query));

  return (
    <section className="clinician-selector" aria-labelledby="clinician-selector-heading">
      <div className="section-heading">
        <h2 id="clinician-selector-heading">Clinician Selection</h2>
        <span className="simulation-pill">FICTIONAL</span>
      </div>
      <label className="filter-field" htmlFor="clinician-search">
        Search fictional clinicians
        <input
          id="clinician-search"
          value={query}
          onChange={(event) => onQueryChange(event.target.value)}
          placeholder="Search by name, specialty, or department"
          aria-describedby={error ? "clinician-error" : undefined}
        />
      </label>
      {error ? (
        <p className="field-error" id="clinician-error">
          {error}
        </p>
      ) : null}

      <div className="clinician-selector__grid">
        <section className="clinician-list" aria-labelledby="clinician-results-heading">
          <h3 id="clinician-results-heading">Search Results</h3>
          {filteredClinicians.length > 0 ? (
            <ul>
              {filteredClinicians.map((clinician) => {
                const isSelected = selectedIdSet.has(clinician.id);
                return (
                  <li key={clinician.id} className="clinician-row">
                    <span className="clinician-avatar" aria-hidden="true">
                      {clinician.initials}
                    </span>
                    <span className="clinician-row__body">
                      <span>{clinician.displayName}</span>
                      <span>
                        {clinician.specialty} · {clinician.department}
                      </span>
                    </span>
                    <button
                      type="button"
                      className="button-secondary"
                      onClick={() => onAdd(clinician.id)}
                      disabled={isSelected}
                    >
                      {isSelected ? `Added ${clinician.displayName}` : `Add ${clinician.displayName}`}
                    </button>
                  </li>
                );
              })}
            </ul>
          ) : (
            <p>No fictional clinicians found.</p>
          )}
        </section>

        <section className="clinician-list" aria-labelledby="selected-clinicians-heading">
          <h3 id="selected-clinicians-heading">Selected Clinicians ({selectedClinicians.length})</h3>
          {selectedClinicians.length > 0 ? (
            <ul>
              {selectedClinicians.map((clinician) => (
                <li key={clinician.id} className="clinician-row">
                  <span className="clinician-avatar" aria-hidden="true">
                    {clinician.initials}
                  </span>
                  <span className="clinician-row__body">
                    <span>{clinician.displayName}</span>
                    <span>
                      {clinician.specialty} · {clinician.department}
                    </span>
                  </span>
                  <button type="button" className="button-secondary" onClick={() => onRemove(clinician.id)}>
                    Remove {clinician.displayName}
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <p>None selected</p>
          )}
        </section>
      </div>
    </section>
  );
}
