import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import * as api from "../lib/alerts";
import { ComposeAlert } from "../features/connected/compose-alert";

vi.mock("../lib/alerts", async importOriginal => ({ ...await importOriginal<typeof api>(), getAlertDraft: vi.fn(), updateAlertDraft: vi.fn(), confirmCriticalField: vi.fn(), setApprovedMessage: vi.fn(), submitAlertDraft: vi.fn() }));
const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
const draft: api.AlertDraft = {
  alertId: "sim-alert", draftVersion: 4, state: "Draft", simulationPatientReference: "SIM-PAT-1", location: "Simulation room", urgencyLabel: "DEMO Urgent", sourceType: "Typed", sourceText: "SIMULATION: original source", sbar: { situation: "SIMULATION: situation", background: "SIMULATION: background", assessment: "SIMULATION: assessment", recommendation: "SIMULATION: recommendation" }, approvedMessage: "SIMULATION: approved message", recipients: [], criticalFields: [{ alertVersion: 4, fieldId: "pulse", originalValue: "118", normalizedValue: "118", unit: "beats/min", status: "Unresolved" }],
};
afterEach(() => vi.clearAllMocks());

test("loads server SBAR and leaves critical values unresolved until an explicit confirmation", async () => {
  vi.mocked(api.getAlertDraft).mockResolvedValue(draft);
  vi.mocked(api.confirmCriticalField).mockResolvedValue({ ...draft, criticalFields: [{ ...draft.criticalFields[0], status: "Confirmed" }] });
  render(<ComposeAlert alertId="sim-alert" />);
  expect(screen.getByRole("status")).toHaveTextContent(/Loading/);
  expect(await screen.findByLabelText("Situation")).toHaveValue("SIMULATION: situation");
  expect(screen.getByText("Unresolved")).toBeVisible();
  expect(api.confirmCriticalField).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Confirm pulse value and unit" }));
  expect(await screen.findByText("Confirmed")).toBeVisible();
  expect(api.confirmCriticalField).toHaveBeenCalledWith("sim-alert", { expectedVersion: 4, fieldId: "pulse", originalValue: "118", normalizedValue: "118", unit: "beats/min" });
});

test("stale save preserves local edits until explicit discard and reload", async () => {
  vi.mocked(api.getAlertDraft).mockResolvedValueOnce(draft).mockResolvedValue({ ...draft, draftVersion: 5, sourceText: "SIMULATION: another operator" });
  vi.mocked(api.updateAlertDraft).mockRejectedValue(new api.AlertApiError(409, "stale-alert-version", "Conflict"));
  render(<ComposeAlert alertId="sim-alert" />);
  fireEvent.change(await screen.findByLabelText("Source text"), { target: { value: "SIMULATION: unsaved change" } });
  fireEvent.click(screen.getByRole("button", { name: "Save source and SBAR" }));
  expect(await screen.findByRole("alert")).toHaveTextContent(/changed.*review/i);
  expect(screen.getByLabelText("Source text")).toHaveValue("SIMULATION: unsaved change");
  expect(screen.getByRole("button", { name: "Save source and SBAR" })).toBeDisabled();
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);
  expect(event.defaultPrevented).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Discard local edits and load server version" }));
  await waitFor(() => expect(screen.getByLabelText("Source text")).toHaveValue("SIMULATION: another operator"));
  expect(api.updateAlertDraft).toHaveBeenCalledTimes(1);
  expect(screen.getByText(/Draft version 5/)).toBeVisible();
});

test("unsaved edits warn on refresh and explicit discard restores saved content", async () => {
  vi.mocked(api.getAlertDraft).mockResolvedValue(draft);
  render(<ComposeAlert alertId="sim-alert" />);
  fireEvent.change(await screen.findByLabelText("Source text"), { target: { value: "SIMULATION: changed" } });
  const event = new Event("beforeunload", { cancelable: true });
  window.dispatchEvent(event);
  expect(event.defaultPrevented).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "Discard unsaved edits" }));
  expect(screen.getByLabelText("Source text")).toHaveValue(draft.sourceText);
});

test("confirmed normalized values are read-only and an unsaved normalization blocks submission", async () => {
  vi.mocked(api.getAlertDraft).mockResolvedValue({ ...draft, criticalFields: [{ ...draft.criticalFields[0], status: "Confirmed" }, { ...draft.criticalFields[0], fieldId: "rate" }] });
  render(<ComposeAlert alertId="sim-alert" />);
  expect(await screen.findByLabelText("Approved value for pulse")).toHaveAttribute("readonly");
  fireEvent.change(screen.getByLabelText("Approved value for rate"), { target: { value: "120" } });
  expect(screen.getByRole("button", { name: "Submit for exact review" })).toBeDisabled();
  fireEvent.change(screen.getByLabelText("Approved secure message"), { target: { value: "SIMULATION: message edit" } });
  expect(screen.getByRole("button", { name: "Approve and save message" })).toBeDisabled();
});

test("confirming one field preserves unsaved normalized edits for another", async () => {
  const two = { ...draft, criticalFields: [...draft.criticalFields, { ...draft.criticalFields[0], fieldId: "rate" }] };
  vi.mocked(api.getAlertDraft).mockResolvedValue(two);
  vi.mocked(api.confirmCriticalField).mockResolvedValue({ ...two, criticalFields: [{ ...two.criticalFields[0], status: "Confirmed", normalizedValue: "119" }, two.criticalFields[1]] });
  render(<ComposeAlert alertId="sim-alert" />);
  fireEvent.change(await screen.findByLabelText("Approved value for pulse"), { target: { value: "119" } });
  fireEvent.change(screen.getByLabelText("Approved value for rate"), { target: { value: "120" } });
  fireEvent.click(screen.getByRole("button", { name: "Confirm pulse value and unit" }));
  await screen.findByText("Confirmed");
  expect(screen.getByLabelText("Approved value for rate")).toHaveValue("120");
});
