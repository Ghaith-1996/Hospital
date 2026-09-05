import React from "react";
import { screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import NewAlertPage from "../app/alerts/new/page";
import AlertReviewPage from "../app/alerts/[id]/review/page";
import DoctorAlertPage from "../app/my-alerts/[id]/page";
import RespondToAlertPage from "../app/my-alerts/[id]/respond/page";
import { createSeedState, DEMO_NOW } from "../features/alerts/seed";
import { PrototypeProvider, prototypeReducer } from "../features/alerts/prototype-store";
import { selectDoctorAlerts } from "../features/alerts/selectors";
import type { AlertStatus, NewAlertInput } from "../features/alerts/types";
import { renderPrototype } from "./test-utils";

vi.mock("next/navigation", () => ({
  useParams: () => ({ id: "alert-critical-1" }),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

function stateWithStatus(status: AlertStatus) {
  const state = createSeedState();
  state.selectedUserId = "user-marc";
  state.alerts = state.alerts.map((alert) => alert.id === "alert-critical-1" ? { ...alert, status } : alert);
  return state;
}

const changedInput: NewAlertInput = {
  patientReference: "SIM-PAT-CHANGED",
  location: "Fictional test location",
  department: "Fictional Emergency",
  urgency: "high",
  caseDetails: "SIMULATION: changed content must not replace a confirmed alert.",
  clinicianIds: ["clinician-julie"],
};

describe("alert workflow boundaries", () => {
  beforeEach(() => window.history.replaceState({}, "", "/"));

  it("hides a draft in every doctor inbox tab until operator confirmation", () => {
    const draft = stateWithStatus("draft");
    for (const tab of ["all", "unread", "in-progress", "completed"] as const) {
      expect(selectDoctorAlerts(draft, "clinician-marc", tab).map((alert) => alert.id)).not.toContain("alert-critical-1");
    }
    const sent = prototypeReducer(draft, { type: "alert-confirmed", alertId: "alert-critical-1", occurredAt: DEMO_NOW });
    expect(selectDoctorAlerts(sent, "clinician-marc", "unread").map((alert) => alert.id)).toContain("alert-critical-1");
  });

  it.each(["draft", "resolved", "cancelled"] as const)("rejects every response to a %s alert without changing history or recipients", (status) => {
    const state = stateWithStatus(status);
    for (const response of ["acknowledged", "accepted", "declined", "unavailable"] as const) {
      expect(prototypeReducer(state, {
        type: "doctor-responded", alertId: "alert-critical-1", clinicianId: "clinician-marc",
        response, note: "SIMULATION: blocked response.", occurredAt: DEMO_NOW,
      })).toEqual(state);
    }
  });

  it.each(["sent", "in-progress", "escalating", "resolved", "cancelled"] as const)("preserves the entire %s record on edit or redispatch", (status) => {
    const state = stateWithStatus(status);
    state.alerts[0].recipients[0] = {
      ...state.alerts[0].recipients[0], response: "accepted", note: "SIMULATION: existing acceptance.",
      responsibilityAcceptedAt: DEMO_NOW,
    };
    expect(prototypeReducer(state, { type: "alert-updated", alertId: "alert-critical-1", input: changedInput })).toEqual(state);
    expect(prototypeReducer(state, { type: "alert-confirmed", alertId: "alert-critical-1", occurredAt: DEMO_NOW })).toEqual(state);
  });

  it("does not expose draft details through a direct doctor URL", () => {
    renderPrototype(<DoctorAlertPage />, { state: stateWithStatus("draft") });
    expect(screen.queryByText("SIM-PAT-01578")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Accept" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to Inbox" })).toBeVisible();
  });

  it.each(["draft", "resolved", "cancelled"] as const)("blocks direct response routes for %s alerts", (status) => {
    renderPrototype(<RespondToAlertPage />, { state: stateWithStatus(status) });
    expect(screen.queryByRole("button", { name: "Submit Response" })).not.toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "Accept" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Back to/ })).toBeVisible();
  });

  it.each(["resolved", "cancelled"] as const)("keeps %s details readable with no response actions", (status) => {
    renderPrototype(<DoctorAlertPage />, { state: stateWithStatus(status) });
    expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Accept" })).not.toBeInTheDocument();
    expect(screen.getByText(/responses are closed/i)).toBeVisible();
  });

  it("blocks editing a sent alert through a stale Back/Edit link", async () => {
    window.history.replaceState({}, "", "/alerts/new?edit=alert-critical-1");
    renderPrototype(<NewAlertPage />, { state: stateWithStatus("sent") });
    expect(await screen.findByRole("link", { name: "View Alert Details" })).toHaveAttribute("href", "/alerts/alert-critical-1");
    expect(screen.queryByRole("textbox", { name: "Case Details" })).not.toBeInTheDocument();
  });

  it("does not offer edit or dispatch when returning to a sent alert's review", () => {
    renderPrototype(<AlertReviewPage />, { state: stateWithStatus("sent") });
    expect(screen.queryByRole("link", { name: "Back/Edit" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Confirm & Dispatch" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View Alert Details" })).toHaveAttribute("href", "/alerts/alert-critical-1");
  });

  it("allows a fresh form after leaving a blocked edit URL", async () => {
    window.history.replaceState({}, "", "/alerts/new?edit=alert-critical-1");
    const state = stateWithStatus("sent");
    const { rerender } = renderPrototype(<NewAlertPage />, { state });
    await screen.findByRole("link", { name: "View Alert Details" });
    window.history.replaceState({}, "", "/alerts/new");
    rerender(<PrototypeProvider initialState={state}><NewAlertPage /></PrototypeProvider>);
    expect(await screen.findByRole("textbox", { name: "Patient Reference" })).toHaveValue("");
  });
});
