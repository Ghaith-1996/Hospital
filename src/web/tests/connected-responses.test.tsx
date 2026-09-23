import React from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import * as api from "../lib/alerts";
import { PractitionerAlert, PractitionerInbox } from "../features/connected/practitioner-alerts";
import { LiveAlert } from "../features/connected/live-alert";
vi.mock("../lib/alerts", async original => ({ ...await original<typeof api>(), getMyAlert: vi.fn(), getMyAlerts: vi.fn(), markMyAlertOpened: vi.fn(), recordMyAlertResponse: vi.fn(), getAlertLive: vi.fn(), resolveAlert: vi.fn(), cancelAlert: vi.fn(), setEscalationPaused: vi.fn() }));
afterEach(() => vi.clearAllMocks());
function escalationLive(): api.AlertLive {
  return {
    alertId: "sim",
    confirmedVersion: 9,
    alertState: "Active",
    outboxState: "Processed",
    refreshedAtUtc: "2026-09-22T12:00:00Z",
    canResolve: false,
    canCancel: true,
    manualFallbackRequired: false,
    recipients: [],
    escalation: {
      policyId: "policy",
      policyVersion: "DEMO-9",
      state: "Scheduled",
      currentStep: 1,
      nextDueAtUtc: "2026-09-22T12:01:00Z",
      remainingDelaySeconds: null,
      stopReason: null,
      canPause: true,
      canResume: false,
      events: [],
    },
  };
}
test("a committed pause with lost response remains retryable after polling hides Pause", async () => {
  const escalation: api.AlertLiveEscalation = { policyId: "policy", policyVersion: "DEMO-9", state: "Scheduled", currentStep: 1,
    nextDueAtUtc: "2026-09-22T12:01:00Z", remainingDelaySeconds: null, stopReason: null, canPause: true, canResume: false, events: [] };
  const live: api.AlertLive = { alertId: "sim", confirmedVersion: 9, alertState: "Active", outboxState: "Processed",
    refreshedAtUtc: "2026-09-22T12:00:00Z", canResolve: false, canCancel: true, manualFallbackRequired: false, recipients: [], escalation };
  vi.mocked(api.getAlertLive).mockResolvedValueOnce(live).mockResolvedValue({ ...live,
    escalation: { ...escalation, state: "Paused", canPause: false, canResume: true, remainingDelaySeconds: 30 } });
  vi.mocked(api.setEscalationPaused).mockRejectedValueOnce(new TypeError("lost response")).mockResolvedValue({} as api.AlertLifecycleResult);
  render(<LiveAlert alertId="sim" pollMs={0} />);
  fireEvent.click(await screen.findByRole("button", { name: "Pause DEMO escalation" }));
  await screen.findByText(/Outcome uncertain/);
  fireEvent.click(screen.getByRole("button", { name: "Refresh status" }));
  await screen.findByText(/Escalation state: Paused/);
  expect(screen.queryByRole("button", { name: "Pause DEMO escalation" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Retry Pause action" }));
  await act(async () => {});
  expect(api.setEscalationPaused).toHaveBeenCalledTimes(2);
  expect(vi.mocked(api.setEscalationPaused).mock.calls[1]).toEqual(vi.mocked(api.setEscalationPaused).mock.calls[0]);
  expect(screen.getByRole("button", { name: "Resume DEMO escalation" })).toBeEnabled();
});
test("escalation uses server state and retries an uncertain pause with the exact same command", async () => {
  const live: api.AlertLive = { alertId: "sim", confirmedVersion: 9, alertState: "Active", outboxState: "Processed", refreshedAtUtc: "2026-09-22T12:00:00Z",
    canResolve: false, canCancel: true, manualFallbackRequired: false, recipients: [],
    escalation: { policyId: "policy", policyVersion: "DEMO-9", state: "Scheduled", currentStep: 1, nextDueAtUtc: "2026-09-22T12:01:00Z",
      remainingDelaySeconds: null, stopReason: null, canPause: true, canResume: false,
      events: [{ sequence: 1, kind: "Scheduled", step: 1, occurredAtUtc: "2026-09-22T12:00:00Z", recipientSelectionId: null, actorUserId: null }] } };
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  vi.mocked(api.setEscalationPaused).mockRejectedValueOnce(new TypeError("offline")).mockResolvedValue({} as api.AlertLifecycleResult);
  render(<LiveAlert alertId="sim" pollMs={0} />);
  const pause = await screen.findByRole("button", { name: "Pause DEMO escalation" });
  expect(screen.getByText(/DEMO-9/)).toBeVisible();
  fireEvent.click(pause); fireEvent.click(pause);
  expect(await screen.findByText(/Outcome uncertain/)).toBeVisible();
  expect(api.setEscalationPaused).toHaveBeenCalledTimes(1);
  expect(screen.getByRole("button", { name: "Cancel simulation alert" })).toBeDisabled();
  fireEvent.click(pause);
  await act(async () => {});
  expect(api.setEscalationPaused).toHaveBeenCalledTimes(2);
  expect(vi.mocked(api.setEscalationPaused).mock.calls[0]).toEqual(vi.mocked(api.setEscalationPaused).mock.calls[1]);
  expect(vi.mocked(api.setEscalationPaused).mock.calls[0].slice(0, 3)).toEqual(["sim", 9, true]);
});
test("operational warning guidance is safe and preserves escalation controls", async () => {
  vi.mocked(api.getAlertLive).mockResolvedValue({ ...escalationLive(), alertId: "sim", operationalWarnings: [{
    code: "ProviderUnavailable", title: "Provider unavailable", explanation: "The simulated notification provider is unavailable.",
    recommendedApplicationAction: "Refresh status. Do not create a duplicate alert. REQUIRES_HOSPITAL_DECISION.", requiresHospitalFallback: true,
  }] } as api.AlertLive);
  render(<LiveAlert alertId="sim" pollMs={0} />);
  expect(await screen.findByRole("heading", { name: "Provider unavailable" })).toBeVisible();
  expect(screen.getByText(/Do not create a duplicate alert/)).toBeVisible();
  expect(screen.getByRole("button", { name: "Pause DEMO escalation" })).toBeVisible();
});
test("untrusted warning text never reaches the live screen", async () => {
  const sentinel = "SIM-APPROVED-MESSAGE-DO-NOT-LOG";
  vi.mocked(api.getAlertLive).mockResolvedValue({ ...escalationLive(), alertId: "sim", operationalWarnings: [{
    code: "ProviderUnavailable", title: sentinel, explanation: sentinel,
    recommendedApplicationAction: sentinel, requiresHospitalFallback: true,
  }] } as api.AlertLive);
  render(<LiveAlert alertId="sim" pollMs={0} />);
  expect(await screen.findByRole("heading", { name: "Provider unavailable" })).toBeVisible();
  expect(document.body.textContent?.includes(sentinel)).toBe(false);
});
test("live polling stops on unmount", async () => {
  vi.useFakeTimers();
  vi.mocked(api.getAlertLive).mockRejectedValue(new TypeError("offline"));
  try {
    const { unmount } = render(<LiveAlert alertId="sim" pollMs={5000} />);
    await act(async () => {});
    await act(() => vi.advanceTimersByTimeAsync(5000));
    expect(api.getAlertLive).toHaveBeenCalledTimes(2);
    unmount();
    await act(() => vi.advanceTimersByTimeAsync(15000));
    expect(api.getAlertLive).toHaveBeenCalledTimes(2);
  } finally { vi.useRealTimers(); }
});
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
