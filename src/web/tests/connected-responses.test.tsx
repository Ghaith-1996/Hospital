import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import * as api from "../lib/alerts";
import { PractitionerAlert, PractitionerInbox } from "../features/connected/practitioner-alerts";
import { LiveAlert } from "../features/connected/live-alert";
vi.mock("../lib/alerts", async original => ({ ...await original<typeof api>(), getMyAlert: vi.fn(), getMyAlerts: vi.fn(), markMyAlertOpened: vi.fn(), recordMyAlertResponse: vi.fn(), getAlertLive: vi.fn(), resolveAlert: vi.fn(), cancelAlert: vi.fn() }));
afterEach(() => vi.clearAllMocks());
test("practitioner acknowledgement does not imply responsibility and explicit open is separate", async () => {
  const detail = { alertId: "sim", confirmedVersion: 9, state: "Active", simulationPatientReference: "SIM-PAT-1", location: "Fictional room", urgencyLabel: "DEMO Urgent", approvedMessage: "SIMULATION: approved", criticalFields: [{ fieldId: "pulse", value: "118", unit: "beats/min" }], channels: ["SecureMessage"], openedState: "NotObserved", secureMessageOpenedAtUtc: null, acknowledgedAtUtc: null, terminalDisposition: null, responsibilityAcceptedAtUtc: null, callUnitRequestedAtUtc: null } as unknown as api.MyAlertDetail;
  vi.mocked(api.getMyAlert).mockResolvedValueOnce(detail).mockResolvedValue({ ...detail, acknowledgedAtUtc: "2026-09-05T12:00:00Z" });
  vi.mocked(api.recordMyAlertResponse).mockResolvedValue({} as api.RecipientResponseResult);
  render(<PractitionerAlert alertId="sim" />);
  expect(await screen.findByText(/118.*beats\/min/)).toBeVisible();
  expect(api.markMyAlertOpened).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Acknowledge" }));
  expect(await screen.findByText(/Acknowledged: 2026/)).toBeVisible();
  expect(screen.getByText("Responsibility accepted: Not recorded")).toBeVisible();
  expect(screen.getByRole("button", { name: "Accept responsibility" })).toBeEnabled();
});
test("inbox authorization failure shows guidance instead of local fictional alerts", async () => {
  vi.mocked(api.getMyAlerts).mockRejectedValue(new api.AlertApiError(403, null, "Forbidden"));
  render(<PractitionerInbox />);
  expect(await screen.findByRole("alert")).toHaveTextContent(/not authorized/);
});
test("durable live failure and response dimensions stay separate", async () => {
  vi.mocked(api.getAlertLive).mockResolvedValue({ alertId: "sim", confirmedVersion: 9, alertState: "Active", outboxState: "Completed", refreshedAtUtc: "2026-09-05T12:00:00Z", canResolve: false, canCancel: true, manualFallbackRequired: true, recipients: [{ practitionerId: "p", simulationCode: "SIM-PRAC-1", displayName: "Fictional Doctor", specialty: "Emergency", onCallSnapshot: "Primary", acknowledgedAtUtc: null, terminalDisposition: null, responsibilityAcceptedAtUtc: null, callUnitRequestedAtUtc: null, lastResponseReasonCode: null, attempts: [{ channel: "Sms", attemptNumber: 1, status: "Failed", openedState: "NotApplicable", openedAtUtc: null, requestedAtUtc: "2026-09-05T12:00:00Z", submittedAtUtc: null, deliveredAtUtc: null, failedAtUtc: "2026-09-05T12:01:00Z", failureCategory: "provider-outage" }] }] });
  render(<LiveAlert alertId="sim" pollMs={0} />);
  expect(await screen.findByText("provider-outage")).toBeVisible();
  expect(screen.getByText(/REQUIRES_HOSPITAL_DECISION/)).toBeVisible();
  expect(screen.getByText(/Opened: NotApplicable/)).toBeVisible();
  expect(screen.getByText("Responsibility accepted: Not recorded")).toBeVisible();
  expect(screen.queryByRole("button", { name: "Resolve simulation alert" })).not.toBeInTheDocument();
});
