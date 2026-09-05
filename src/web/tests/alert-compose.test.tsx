import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import NewAlertPage from "../app/alerts/new/page";
import AlertComposePage from "../app/alerts/[id]/compose/page";

const navigation = vi.hoisted(() => ({
  push: vi.fn(),
  params: { id: "00000000-0000-0000-0000-000000000001" },
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push: navigation.push }),
  useParams: () => navigation.params,
}));

vi.mock("next/link", () => ({
  default: ({ href, children, className }: { href: string; children: React.ReactNode; className?: string }) =>
    React.createElement("a", { href, className }, children),
}));

const draft = {
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
      alertVersion: 1,
      fieldId: "heartRate",
      originalValue: "118",
      normalizedValue: "118",
      unit: "beats/min",
      status: "Unresolved",
    },
  ],
  approvedMessage: null,
  recipients: [],
};

describe("Phase 6 compose flow", () => {
  beforeEach(() => {
    navigation.push.mockReset();
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const path = String(input);
      if (path.endsWith("/api/v1/dev/identities")) {
        return { ok: false, status: 404, json: async () => ({}) };
      }
      if (path.endsWith("/api/v1/alerts/drafts")) {
        return { ok: true, status: 201, json: async () => draft };
      }
      if (init?.method === "PATCH") {
        return { ok: true, status: 200, json: async () => ({ ...draft, draftVersion: 2 }) };
      }
      return { ok: true, status: 200, json: async () => draft };
    }));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("creates a typed draft and routes to its dynamic compose page", async () => {
    render(<NewAlertPage />);

    fireEvent.click(screen.getByRole("button", { name: "Create draft" }));

    expect(await screen.findByText("Draft created. Opening the compose workspace.")).toBeVisible();
    expect(navigation.push).toHaveBeenCalledWith(`/alerts/${draft.alertId}/compose`);
  });

  it("loads the protected draft, edits approved content, and preserves the visible version", async () => {
    const loadedDraft = {
      ...draft,
      draftVersion: 3,
      approvedMessage: "SIMULATION: approved message for review",
    };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo, init?: RequestInit) => {
        const path = String(input);
        if (path.endsWith("/api/v1/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }
        if (init?.method === "PUT" && path.endsWith("/approved-message")) {
          return { ok: true, status: 200, json: async () => ({ ...loadedDraft, draftVersion: 4 }) };
        }
        if (init?.method === "PATCH") {
          return { ok: true, status: 200, json: async () => ({ ...loadedDraft, draftVersion: 4 }) };
        }
        return { ok: true, status: 200, json: async () => loadedDraft };
      }),
    );

    render(<AlertComposePage />);

    expect(await screen.findByText(/Draft version: 3/)).toBeVisible();
    expect(screen.getByLabelText("Approved message")).toHaveValue("SIMULATION: approved message for review");
    fireEvent.change(screen.getByLabelText("Approved message"), {
      target: { value: "SIMULATION: revised approved message" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save approved message" }));

    expect(await screen.findByText(/Draft version: 4/)).toBeVisible();
    const approvedCall = vi.mocked(fetch).mock.calls.find(([input, init]) =>
      String(input).endsWith("/approved-message") && init?.method === "PUT",
    );
    expect(approvedCall).toBeDefined();
    expect(JSON.parse(String(approvedCall![1]?.body))).toEqual({
      expectedVersion: 3,
      approvedMessage: "SIMULATION: revised approved message",
    });
  });

  it("confirms the current critical field and links a pending draft to exact review", async () => {
    const confirmedDraft = {
      ...draft,
      criticalFields: [{ ...draft.criticalFields[0], status: "Confirmed" }],
    };
    const pendingDraft = { ...confirmedDraft, state: "PendingConfirmation" };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        const path = String(input);
        if (path.endsWith("/api/v1/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }
        if (path.endsWith("/field-confirmations")) {
          return { ok: true, status: 200, json: async () => confirmedDraft };
        }
        if (path.endsWith("/submit-for-confirmation")) {
          return { ok: true, status: 200, json: async () => pendingDraft };
        }
        return { ok: true, status: 200, json: async () => draft };
      }),
    );

    render(<AlertComposePage />);
    fireEvent.click(await screen.findByRole("button", { name: "Confirm heartRate" }));
    expect(await screen.findByText(/Critical field confirmed/)).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Submit for confirmation" }));

    expect(await screen.findByText("Draft version: 1 · State: PendingConfirmation")).toBeVisible();
    expect(screen.getByRole("link", { name: "Open exact review" })).toHaveAttribute(
      "href",
      `/alerts/${draft.alertId}/review`,
    );
    expect(screen.queryByText(/delivery/i)).not.toBeInTheDocument();
  });

  it("shows reload guidance for a stale compose save", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({ ok: false, status: 409, json: async () => ({ detail: "draft-version-stale" }) })),
    );
    render(<AlertComposePage />);

    expect(await screen.findByText("This draft changed elsewhere. Reload it before saving again.")).toBeVisible();
  });
});
