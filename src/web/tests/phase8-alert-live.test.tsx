import React from "react";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import AlertLivePage from "../app/alerts/[id]/live/page";

const navigation = vi.hoisted(() => ({
  params: { id: "00000000-0000-0000-0000-000000000801" },
}));

vi.mock("next/navigation", () => ({
  useParams: () => navigation.params,
}));

vi.mock("next/link", () => ({
  default: ({ href, children, className }: { href: string; children: React.ReactNode; className?: string }) =>
    React.createElement("a", { href, className }, children),
}));

const live = {
  alertId: navigation.params.id,
  confirmedVersion: 3,
  alertState: "Active",
  outboxState: "Processed",
  refreshedAtUtc: "2026-08-30T16:05:00Z",
  recipients: [
    {
      practitionerId: "00000000-0000-0000-0000-000000000108",
      simulationCode: "SIM-PRAC-0108",
      displayName: "Riley Sato",
      specialty: "Neurology",
      onCallSnapshot: "Primary",
      acknowledgedAtUtc: "2026-08-30T16:02:00Z",
      terminalDisposition: "Accepted",
      responsibilityAcceptedAtUtc: "2026-08-30T16:03:00Z",
      attempts: [
        {
          channel: "SecureMessage",
          attemptNumber: 1,
          status: "Delivered",
          openedState: "Occurred",
          openedAtUtc: "2026-08-30T16:01:00Z",
          requestedAtUtc: "2026-08-30T16:00:00Z",
          submittedAtUtc: "2026-08-30T16:00:20Z",
          deliveredAtUtc: "2026-08-30T16:00:40Z",
          failedAtUtc: null,
          failureCategory: null,
        },
      ],
    },
  ],
};

describe("Phase 8 operator live projection", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("labels refreshed polling status and cleans up the polling interval", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo) =>
      String(input).endsWith("/api/dev/identities")
        ? { ok: false, status: 404, json: async () => ({}) }
        : { ok: true, status: 200, json: async () => live },
    );
    vi.stubGlobal("fetch", fetchMock);
    const clearIntervalSpy = vi.spyOn(globalThis, "clearInterval");

    const view = render(<AlertLivePage />);

    expect(await screen.findByRole("heading", { name: "Riley Sato" })).toBeVisible();
    expect(screen.getByText("Refreshed status at 2026-08-30T16:05:00.000Z")).toBeVisible();
    expect(screen.getByText("This page polls for refreshed status; it is not guaranteed real-time monitoring.")).toBeVisible();
    expect(screen.getByText("Acknowledgement: recorded at 2026-08-30T16:02:00.000Z")).toBeVisible();
    expect(screen.getByText("Terminal disposition: Accepted")).toBeVisible();
    expect(screen.getByText("Responsibility: accepted at 2026-08-30T16:03:00.000Z")).toBeVisible();
    expect(screen.getByText("SecureMessage attempt 1")).toBeVisible();
    expect(screen.getByText("Delivery: Delivered")).toBeVisible();
    expect(screen.getByText("Opened: occurred at 2026-08-30T16:01:00.000Z")).toBeVisible();

    view.unmount();
    expect(clearIntervalSpy).toHaveBeenCalled();
  });
});
