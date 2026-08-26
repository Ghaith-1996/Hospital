import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AlertComposePage from "../app/alerts/new/page";

vi.mock("next/link", () => ({
  default: ({ href, children, className }: { href: string; children: React.ReactNode; className?: string }) =>
    React.createElement("a", { href, className }, children),
}));

const createdDraft = {
  alertId: "00000000-0000-0000-0000-000000000001",
  state: "Draft",
  draftVersion: 1,
  simulationPatientReference: "SIM-PAT-0001",
  location: "North Wing / Simulation Room 204",
  urgencyLabel: "Urgent",
  sourceType: "Typed",
  sourceText: "SIMULATION: fictional typed source",
  sbar: {
    situation: "SIMULATION: fictional situation",
    background: "SIMULATION: fictional background",
    assessment: "SIMULATION: fictional assessment",
    recommendation: "SIMULATION: fictional recommendation",
  },
  criticalFields: [
    {
      fieldId: "heartRate",
      originalValue: "118",
      normalizedValue: "118",
      unit: "beats/min",
      status: "Unresolved",
    },
  ],
};

describe("Phase 5 alert compose", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        if (String(input).includes("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }

        return { ok: true, status: 201, json: async () => createdDraft };
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders required typed SBAR fields and no dispatch control", () => {
    render(<AlertComposePage />);

    expect(screen.getByRole("heading", { name: "Typed alert drafting" })).toBeVisible();
    expect(screen.getByLabelText("Synthetic patient reference")).toBeRequired();
    expect(screen.getByLabelText("Situation")).toBeRequired();
    expect(screen.getByLabelText("Recommendation")).toBeRequired();
    expect(screen.queryByRole("button", { name: /dispatch/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /recipient/i })).not.toBeInTheDocument();
  });

  it("creates a typed simulation draft and shows versioned confirmation state", async () => {
    const fetchMock = vi.mocked(fetch);
    render(<AlertComposePage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));

    expect(await screen.findByText(/Draft version: 1/)).toBeVisible();
    expect(screen.getByRole("button", { name: "Confirm heartRate" })).toBeVisible();
    const draftCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/api/alerts/drafts"));
    expect(draftCall).toBeDefined();
    const request = draftCall![1] as RequestInit;
    expect(JSON.parse(String(request.body))).toMatchObject({
      simulationPatientReference: "SIM-PAT-0001",
      sourceText: "SIMULATION: fictional typed source",
      sbar: { situation: "SIMULATION: fictional situation" },
    });
  });

  it("shows stale-version guidance when the server rejects an edit", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        if (String(input).includes("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }

        return { ok: false, status: 409, json: async () => ({ detail: "draft-version-stale" }) };
      }),
    );
    render(<AlertComposePage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));

    expect(await screen.findByText("This draft changed elsewhere. Reload it before saving again.")).toBeVisible();
  });
});
