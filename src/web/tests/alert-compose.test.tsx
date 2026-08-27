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

  it("edits the current draft version with typed source and SBAR content", async () => {
    const updatedDraft = {
      ...createdDraft,
      draftVersion: 2,
      location: "North Wing / Simulation Room 205",
      sbar: {
        ...createdDraft.sbar,
        situation: "SIMULATION: revised fictional situation",
      },
    };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo, init?: RequestInit) => {
        if (String(input).includes("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }

        if (init?.method === "PATCH") {
          return { ok: true, status: 200, json: async () => updatedDraft };
        }

        return { ok: true, status: 201, json: async () => createdDraft };
      }),
    );
    const fetchMock = vi.mocked(fetch);
    render(<AlertComposePage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));
    expect(await screen.findByText(/Draft version: 1/)).toBeVisible();
    fireEvent.change(screen.getByLabelText("Simulation location"), {
      target: { value: "North Wing / Simulation Room 205" },
    });
    fireEvent.change(screen.getByLabelText("Situation"), {
      target: { value: "SIMULATION: revised fictional situation" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save draft" }));

    expect(await screen.findByText(/Draft version: 2/)).toBeVisible();
    const updateCall = fetchMock.mock.calls.find(([, init]) => init?.method === "PATCH");
    expect(updateCall).toBeDefined();
    expect(updateCall![0]).toBe(`/api/alerts/${createdDraft.alertId}`);
    expect(JSON.parse(String(updateCall![1]?.body))).toEqual({
      expectedVersion: 1,
      location: "North Wing / Simulation Room 205",
      urgencyLabel: "Urgent",
      sourceText: "SIMULATION: fictional typed source",
      sbar: {
        situation: "SIMULATION: revised fictional situation",
        background: "SIMULATION: fictional background",
        assessment: "SIMULATION: fictional assessment",
        recommendation: "SIMULATION: fictional recommendation",
      },
    });
  });

  it("confirms the exact critical field before submitting the same draft version", async () => {
    const confirmedDraft = {
      ...createdDraft,
      criticalFields: [{ ...createdDraft.criticalFields[0], status: "Confirmed" }],
    };
    const submittedDraft = { ...confirmedDraft, state: "PendingConfirmation" };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        const path = String(input);
        if (path.includes("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }

        if (path.endsWith("/field-confirmations")) {
          return { ok: true, status: 200, json: async () => confirmedDraft };
        }

        if (path.endsWith("/submit-for-confirmation")) {
          return { ok: true, status: 200, json: async () => submittedDraft };
        }

        return { ok: true, status: 201, json: async () => createdDraft };
      }),
    );
    const fetchMock = vi.mocked(fetch);
    render(<AlertComposePage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm heartRate" }));
    expect(await screen.findByText("Critical field confirmed by the authenticated simulation user.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Confirm heartRate" })).not.toBeInTheDocument();

    const confirmationCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/field-confirmations"));
    expect(JSON.parse(String(confirmationCall![1]?.body))).toEqual({
      expectedVersion: 1,
      fieldId: "heartRate",
      originalValue: "118",
      normalizedValue: "118",
      unit: "beats/min",
    });

    fireEvent.click(screen.getByRole("button", { name: "Submit for confirmation" }));
    expect(await screen.findByText(/State: PendingConfirmation/)).toBeVisible();
    const submitCall = fetchMock.mock.calls.find(([input]) => String(input).endsWith("/submit-for-confirmation"));
    expect(JSON.parse(String(submitCall![1]?.body))).toEqual({ expectedVersion: 1 });
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

  it("shows stale-version guidance when submission conflicts", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        if (String(input).includes("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }

        if (String(input).endsWith("/submit-for-confirmation")) {
          return { ok: false, status: 409, json: async () => ({ detail: "draft-version-stale" }) };
        }

        return { ok: true, status: 201, json: async () => createdDraft };
      }),
    );
    render(<AlertComposePage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));
    fireEvent.click(await screen.findByRole("button", { name: "Submit for confirmation" }));

    expect(
      await screen.findByText("This draft changed elsewhere. Reload it before submitting for confirmation."),
    ).toBeVisible();
  });
});
