import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AlertReviewPage from "../app/alerts/[id]/review/page";

const navigation = vi.hoisted(() => ({
  params: { id: "00000000-0000-0000-0000-000000000001" },
}));

vi.mock("next/navigation", () => ({
  useParams: () => navigation.params,
}));

vi.mock("next/link", () => ({
  default: ({ href, children, className }: { href: string; children: React.ReactNode; className?: string }) =>
    React.createElement("a", { href, className }, children),
}));

const review = {
  alertId: "00000000-0000-0000-0000-000000000001",
  draftVersion: 7,
  state: "PendingConfirmation",
  simulationPatientReference: "SIM-PAT-REVIEW-0001",
  location: "North Wing / Simulation Room 204",
  urgencyLabel: "Urgent",
  approvedMessage: "SIMULATION: exact approved message",
  criticalFields: [
    {
      alertVersion: 7,
      fieldId: "heartRate",
      originalValue: "118",
      normalizedValue: "118",
      unit: "beats/min",
      status: "Confirmed",
      confirmedByUserId: "00000000-0000-0000-0000-000000000401",
      confirmedAtUtc: "2026-08-28T12:00:00Z",
    },
  ],
  recipients: [
    {
      practitionerId: "00000000-0000-0000-0000-000000000101",
      displayName: "Maya Chen",
      specialty: "Emergency",
      department: "Fictional Emergency Care",
      site: "North Wing Simulation Site",
      roleTitle: "Emergency physician",
      channel: "SecureMessage",
      selectedAtUtc: "2026-08-28T11:00:00Z",
      directorySourceUpdatedAtUtc: "2026-08-01T12:00:00Z",
      onCallSnapshot: "Primary",
      isStale: false,
      directoryRevision: "SIM-REV-MAYA",
    },
  ],
  demoEscalationPolicyVersion: "DEMO",
  demoNotificationPolicyVersion: "DEMO",
};

describe("Phase 8 exact review", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) =>
        String(input).endsWith("/api/dev/identities")
          ? { ok: false, status: 404, json: async () => ({}) }
          : { ok: true, status: 200, json: async () => review },
      ),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the exact version and requires deliberate confirmation", async () => {
    render(<AlertReviewPage />);

    expect(await screen.findByText("Draft version 7")).toBeVisible();
    expect(screen.getByText("SIMULATION: exact approved message")).toBeVisible();
    expect(screen.getByText("118 beats/min")).toBeVisible();
    expect(screen.getByText("Maya Chen")).toBeVisible();
    expect(screen.getByText("Primary")).toBeVisible();
    expect(screen.getByRole("button", { name: "Confirm and queue simulation alert" })).toBeDisabled();
    expect(screen.getByText(/Development\/Test-only simulation worker/i)).toBeVisible();
    expect(screen.queryByRole("link", { name: /live/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("checkbox", { name: /I reviewed/ }));
    expect(screen.getByRole("button", { name: "Confirm and queue simulation alert" })).toBeEnabled();
  });

  it("disables double-submit and retains the idempotency key until the request resolves", async () => {
    let resolveConfirmation: ((value: unknown) => void) | undefined;
    const confirmation = new Promise((resolve) => {
      resolveConfirmation = resolve;
    });
    const fetchMock = vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      if (String(input).endsWith("/api/dev/identities")) {
        return { ok: false, status: 404, json: async () => ({}) };
      }
      if (init?.method === "POST") {
        await confirmation;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            alertId: review.alertId,
            confirmedVersion: review.draftVersion,
            state: "DispatchQueued",
            replayed: false,
          }),
        };
      }

      return { ok: true, status: 200, json: async () => review };
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<AlertReviewPage />);

    fireEvent.click(await screen.findByRole("checkbox", { name: /I reviewed/ }));
    const button = screen.getByRole("button", { name: "Confirm and queue simulation alert" });
    fireEvent.click(button);
    fireEvent.click(button);

    expect(button).toBeDisabled();
    expect(fetchMock.mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1);
    resolveConfirmation?.({});

    expect(await screen.findByText("Simulation alert queued for simulation dispatch.")).toBeVisible();
    expect(screen.getByRole("link", { name: "Open refreshed live status" })).toHaveAttribute(
      "href",
      `/alerts/${review.alertId}/live`,
    );
    const confirmCall = fetchMock.mock.calls.find(([, init]) => init?.method === "POST");
    expect(confirmCall).toBeDefined();
    expect((confirmCall![1]?.headers as Record<string, string>) ["Idempotency-Key"]).toMatch(/^[A-Za-z0-9-]+$/);
  });
});
