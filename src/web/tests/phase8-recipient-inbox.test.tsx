import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import MyAlertsPage from "../app/my-alerts/page";
import MyAlertDetailPage from "../app/my-alerts/[id]/page";

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

const summary = {
  alertId: navigation.params.id,
  confirmedVersion: 3,
  state: "Active",
  location: "North Wing Simulation Room 8",
  urgencyLabel: "DEMO-URGENT",
  confirmedAtUtc: "2026-08-30T16:00:00Z",
  channels: ["SecureMessage", "Sms"],
  openedState: "PendingNotObserved",
  acknowledgedAtUtc: null,
  terminalDisposition: null,
  responsibilityAcceptedAtUtc: null,
};

const detail = {
  ...summary,
  simulationPatientReference: "SIM-PAT-PHASE8",
  approvedMessage: "SIMULATION: fictional Phase 8 approved message",
  criticalFields: [{ fieldId: "heartRate", normalizedValue: "118", unit: "beats/min" }],
  secureMessageOpenedAtUtc: null,
};

function identityResponse() {
  return { ok: false, status: 404, json: async () => ({}) };
}

describe("Phase 8 practitioner inbox", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) =>
        String(input).endsWith("/api/dev/identities")
          ? identityResponse()
          : { ok: true, status: 200, json: async () => [summary] },
      ),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders only the server-scoped inbox with explicit independent states", async () => {
    render(<MyAlertsPage />);

    expect(await screen.findByRole("heading", { name: "North Wing Simulation Room 8" })).toBeVisible();
    expect(screen.getByText("Channels: SecureMessage, Sms")).toBeVisible();
    expect(screen.getByText("Opened: pending, not observed")).toBeVisible();
    expect(screen.getByText("Acknowledgement: not recorded")).toBeVisible();
    expect(screen.getByText("Terminal disposition: not recorded")).toBeVisible();
    expect(screen.getByText("Responsibility: not accepted")).toBeVisible();
    expect(screen.getByRole("link", { name: "Open addressed alert" })).toHaveAttribute(
      "href",
      `/my-alerts/${summary.alertId}`,
    );
    expect(fetch).toHaveBeenCalledWith("/api/my-alerts", expect.objectContaining({ credentials: "include" }));
  });

  it("shows a safe role-specific error without exposing server details", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) =>
        String(input).endsWith("/api/dev/identities")
          ? identityResponse()
          : {
              ok: false,
              status: 403,
              json: async () => ({ title: "Forbidden", detail: "internal-role-layout" }),
            },
      ),
    );

    render(<MyAlertsPage />);

    expect(await screen.findByText("This identity does not have a practitioner inbox.")).toBeVisible();
    expect(screen.queryByText("internal-role-layout")).not.toBeInTheDocument();
  });

  it("labels a channel without open observation as not applicable", async () => {
    const smsOnlySummary = { ...summary, channels: ["Sms"], openedState: "NotApplicable" };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) =>
        String(input).endsWith("/api/dev/identities")
          ? identityResponse()
          : { ok: true, status: 200, json: async () => [smsOnlySummary] },
      ),
    );

    render(<MyAlertsPage />);

    expect(await screen.findByText("Opened: not applicable")).toBeVisible();
  });
});

describe("Phase 8 practitioner response detail", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("records SecureMessage open on detail load and explains independent response semantics", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const path = String(input);
      if (path.endsWith("/api/dev/identities")) return identityResponse();
      if (path.endsWith("/opened") && init?.method === "POST") {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            alertId: summary.alertId,
            confirmedVersion: summary.confirmedVersion,
            secureMessageOpenedAtUtc: "2026-08-30T16:01:00Z",
            replayed: false,
          }),
        };
      }
      return { ok: true, status: 200, json: async () => detail };
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<MyAlertDetailPage />);

    expect(await screen.findByText(detail.approvedMessage)).toBeVisible();
    expect(screen.getByText("118 beats/min")).toBeVisible();
    expect(screen.getByText("Acknowledgement does not accept responsibility.")).toBeVisible();
    expect(screen.getByText("Acceptance records responsibility but does not resolve this alert.")).toBeVisible();
    expect(await screen.findByText(/SecureMessage opened at 2026-08-30T16:01:00.000Z/)).toBeVisible();
    const openCall = fetchMock.mock.calls.find(([input, init]) =>
      String(input).endsWith("/opened") && init?.method === "POST",
    );
    expect(openCall).toBeDefined();
    expect(JSON.parse(String(openCall![1]?.body))).toEqual({ expectedVersion: detail.confirmedVersion });
  });

  it("does not post an open observation for a non-SecureMessage alert", async () => {
    const smsOnlyDetail = { ...detail, channels: ["Sms"], openedState: "NotApplicable" };
    const fetchMock = vi.fn(async (input: RequestInfo) =>
      String(input).endsWith("/api/dev/identities")
        ? identityResponse()
        : { ok: true, status: 200, json: async () => smsOnlyDetail },
    );
    vi.stubGlobal("fetch", fetchMock);

    render(<MyAlertDetailPage />);

    expect(await screen.findByText(detail.approvedMessage)).toBeVisible();
    expect(screen.getByText("Opened: not applicable")).toBeVisible();
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/opened"))).toHaveLength(0);
  });

  it("prevents double-submit while a deliberate terminal response is pending", async () => {
    let resolveResponse: ((value: unknown) => void) | undefined;
    const pendingResponse = new Promise((resolve) => {
      resolveResponse = resolve;
    });
    const fetchMock = vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const path = String(input);
      if (path.endsWith("/api/dev/identities")) return identityResponse();
      if (path.endsWith("/opened")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            alertId: summary.alertId,
            confirmedVersion: summary.confirmedVersion,
            secureMessageOpenedAtUtc: "2026-08-30T16:01:00Z",
            replayed: false,
          }),
        };
      }
      if (path.endsWith("/responses") && init?.method === "POST") {
        await pendingResponse;
        return {
          ok: true,
          status: 200,
          json: async () => ({
            alertId: summary.alertId,
            confirmedVersion: summary.confirmedVersion,
            responseType: "Accepted",
            acknowledgedAtUtc: null,
            terminalDisposition: "Accepted",
            responsibilityAcceptedAtUtc: "2026-08-30T16:02:00Z",
            replayed: false,
          }),
        };
      }
      return { ok: true, status: 200, json: async () => detail };
    });
    vi.stubGlobal("fetch", fetchMock);
    render(<MyAlertDetailPage />);

    const accept = await screen.findByRole("button", { name: "Accept responsibility" });
    fireEvent.click(accept);
    fireEvent.click(accept);

    expect(accept).toBeDisabled();
    expect(fetchMock.mock.calls.filter(([input]) => String(input).endsWith("/responses"))).toHaveLength(1);
    resolveResponse?.({});
    expect(await screen.findByText("Responsibility accepted. The alert remains active.")).toBeVisible();
    expect(screen.getByRole("button", { name: "Decline" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Mark unavailable" })).toBeDisabled();
  });
});
