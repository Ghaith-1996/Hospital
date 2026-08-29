import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import AlertRecipientsPage from "../app/alerts/[id]/recipients/page";

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
  draftVersion: 2,
  simulationPatientReference: "SIM-PAT-0001",
  location: "North Wing / Simulation Room 204",
  urgencyLabel: "Urgent",
  sourceType: "Typed",
  sourceText: "SIMULATION: source",
  sbar: null,
  criticalFields: [],
  approvedMessage: "SIMULATION: approved message",
  recipients: [],
};

const maya = {
  practitionerId: "00000000-0000-0000-0000-000000000101",
  displayName: "Maya Chen",
  firstName: "Maya",
  lastName: "Chen",
  specialty: "Emergency",
  department: "Fictional Emergency Care",
  site: "North Wing Simulation Site",
  roleTitle: "Emergency physician",
  simulationCode: "SIM-PRAC-0101",
  isActive: true,
  isStale: false,
  selectable: true,
  sourceSystem: "SIM-CSV",
  lastSynchronizedAtUtc: "2026-08-01T12:00:00Z",
  onCallTier: "Primary",
  onCallSourceSystem: "SIM-CSV",
  onCallLastSynchronizedAtUtc: "2026-08-01T12:00:00Z",
  practitionerRoleId: "00000000-0000-0000-0000-000000000701",
  availableChannels: ["SecureMessage", "Sms"],
  selectionRevision: "SIM-REV-MAYA",
};

describe("Phase 6 recipient selection", () => {
  beforeEach(() => {
    navigation.push.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("starts unchecked, filters the fictional directory, and saves one complete recipient set", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo, init?: RequestInit) => {
      const path = String(input);
      if (path.endsWith("/api/dev/identities")) {
        return { ok: false, status: 404, json: async () => ({}) };
      }
      if (init?.method === "PUT") {
        return { ok: true, status: 200, json: async () => ({ ...draft, draftVersion: 3 }) };
      }
      if (path.includes("/api/alerts/")) {
        return { ok: true, status: 200, json: async () => draft };
      }
      return { ok: true, status: 200, json: async () => [maya] };
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<AlertRecipientsPage />);

    const secureMessage = await screen.findByRole("checkbox", { name: "Maya Chen — SecureMessage" });
    const sms = screen.getByRole("checkbox", { name: "Maya Chen — Sms" });
    expect(secureMessage).not.toBeChecked();
    expect(sms).not.toBeChecked();
    fireEvent.change(screen.getByLabelText("Department filter"), { target: { value: "Fictional Emergency Care" } });
    fireEvent.click(screen.getByRole("button", { name: "Search directory" }));
    fireEvent.click(secureMessage);

    expect(screen.getByText("Selected recipients (1)")).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "Save recipient set" }));

    expect(await screen.findByText("Recipient set saved. Critical fields must be reconfirmed for the new version.")).toBeVisible();
    expect(navigation.push).toHaveBeenCalledWith(
      "/alerts/00000000-0000-0000-0000-000000000001/compose?recipientsSaved=1",
    );
    const recipientCall = fetchMock.mock.calls.find(([input, init]) =>
      String(input).endsWith("/recipients") && init?.method === "PUT",
    );
    expect(recipientCall).toBeDefined();
    expect(JSON.parse(String(recipientCall![1]?.body))).toEqual({
      expectedVersion: 2,
      recipients: [
        {
          practitionerId: maya.practitionerId,
          practitionerRoleId: maya.practitionerRoleId,
          channel: "SecureMessage",
          directoryRevision: maya.selectionRevision,
        },
      ],
    });
    expect(JSON.stringify(recipientCall![1]?.body)).not.toContain("sim-secure://");
  });

  it("shows reload guidance when a presented directory revision is stale", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo, init?: RequestInit) => {
        const path = String(input);
        if (path.endsWith("/api/dev/identities")) {
          return { ok: false, status: 404, json: async () => ({}) };
        }
        if (init?.method === "PUT") {
          return { ok: false, status: 409, json: async () => ({ detail: "directory-revision-stale" }) };
        }
        if (path.includes("/api/alerts/")) {
          return { ok: true, status: 200, json: async () => draft };
        }
        return { ok: true, status: 200, json: async () => [maya] };
      }),
    );

    render(<AlertRecipientsPage />);
    fireEvent.click(await screen.findByRole("checkbox", { name: "Maya Chen — SecureMessage" }));
    fireEvent.click(screen.getByRole("button", { name: "Save recipient set" }));

    expect(await screen.findByText("The directory changed. Reload the results and reselect recipients.")).toBeVisible();
  });
});
