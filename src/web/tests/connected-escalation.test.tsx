import React from "react";
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import * as api from "../lib/alerts";
import { LiveAlert } from "../features/connected/live-alert";
import { escalationLive } from "./fixtures/escalation-live";

vi.mock("../lib/alerts", async original => ({ ...await original<typeof api>(), getAlertLive: vi.fn(), pauseEscalation: vi.fn(), resumeEscalation: vi.fn(), cancelAlert: vi.fn(), resolveAlert: vi.fn() }));
afterEach(() => { vi.resetAllMocks(); vi.useRealTimers(); });

test("pause double click submits the exact version and selected reason once", async () => {
  const live = escalationLive();
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  let finish!: (value: api.EscalationOverrideResult) => void;
  vi.mocked(api.pauseEscalation).mockReturnValue(new Promise(resolve => { finish = resolve; }));
  render(<LiveAlert alertId={live.alertId} pollMs={0} />);
  await screen.findByRole("button", { name: "Pause escalation" });
  fireEvent.change(screen.getByLabelText("Pause reason"), { target: { value: "ManualCoordination" } });
  fireEvent.click(screen.getByRole("button", { name: "Pause escalation" }));
  fireEvent.click(screen.getByRole("button", { name: "Pause escalation" }));
  expect(api.pauseEscalation).toHaveBeenCalledTimes(1);
  expect(api.pauseEscalation).toHaveBeenCalledWith("sim-alert", 9, "ManualCoordination", expect.any(String));
  vi.mocked(api.getAlertLive).mockResolvedValue({ ...live, escalation: { ...live.escalation, runState: "Paused", paused: true, canPause: false, canResume: true, nextEvaluationAtUtc: null, remainingPauseSeconds: 37 } });
  await act(async () => finish({ alertId: live.alertId, confirmedVersion: 9, escalationRunId: "sim-run", state: "Paused", reasonCode: "ManualCoordination", occurredAtUtc: live.refreshedAtUtc, replayed: false }));
  expect(await screen.findByText(/Remaining delay: 37 seconds/)).toBeVisible();
});

test.each(["Pause", "Resume"] as const)("lost %s response retains an explicit original retry after polling changes allowed actions", async label => {
  const live = escalationLive();
  const pause = label === "Pause";
  live.escalation = { ...live.escalation, runState: pause ? "Scheduled" : "Paused", paused: !pause, canPause: pause, canResume: !pause };
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  const command = vi.mocked(pause ? api.pauseEscalation : api.resumeEscalation);
  command.mockRejectedValueOnce(new TypeError("lost response")).mockResolvedValue({} as api.EscalationOverrideResult);
  render(<LiveAlert alertId={live.alertId} pollMs={0} />);
  fireEvent.click(await screen.findByRole("button", { name: `${label} escalation` }));
  await screen.findByRole("button", { name: `Retry ${label} request` });
  const original = command.mock.calls[0];
  vi.mocked(api.getAlertLive).mockResolvedValue({ ...live, escalation: { ...live.escalation, runState: pause ? "Paused" : "Scheduled", paused: pause, canPause: !pause, canResume: pause } });
  fireEvent.click(screen.getByRole("button", { name: "Refresh status" }));
  const opposite = pause ? "Resume" : "Pause";
  await waitFor(() => expect(screen.getByRole("button", { name: `${opposite} escalation` })).toBeDisabled());
  fireEvent.click(screen.getByRole("button", { name: `Retry ${label} request` }));
  await waitFor(() => expect(command).toHaveBeenCalledTimes(2));
  expect(command.mock.calls[1]).toEqual(original);
  await waitFor(() => expect(screen.getByRole("button", { name: `${opposite} escalation` })).toBeEnabled());
});

test("resume double click sends one request and a failed refresh prevents a new command", async () => {
  const live = escalationLive();
  live.escalation = { ...live.escalation, paused: true, runState: "Paused", canPause: false, canResume: true };
  vi.mocked(api.getAlertLive).mockResolvedValueOnce(live).mockRejectedValue(new TypeError("offline"));
  let finish!: (value: api.EscalationOverrideResult) => void;
  vi.mocked(api.resumeEscalation).mockReturnValue(new Promise(resolve => { finish = resolve; }));
  render(<LiveAlert alertId="sim-alert" pollMs={0} />);
  const resume = await screen.findByRole("button", { name: "Resume escalation" });
  fireEvent.click(resume); fireEvent.click(resume);
  expect(api.resumeEscalation).toHaveBeenCalledTimes(1);
  expect(api.resumeEscalation).toHaveBeenCalledWith("sim-alert", 9, expect.any(String));
  await act(async () => finish({} as api.EscalationOverrideResult));
  expect(await screen.findByText(/The action succeeded, but current server status is unavailable/)).toBeVisible();
  expect(resume).toBeDisabled();
  expect(screen.queryByRole("button", { name: "Retry Resume request" })).not.toBeInTheDocument();
});

test("typed control client sends mandatory exact-version, reason and retained key without extra fields", async () => {
  const client = await vi.importActual<typeof api>("../lib/alerts");
  const fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({}) });
  vi.stubGlobal("fetch", fetch);
  try {
    await client.pauseEscalation("sim-alert", 9, "ManualCoordination", "retained-pause-key");
    await client.resumeEscalation("sim-alert", 9, "retained-resume-key");
    expect(fetch.mock.calls[0][0]).toBe("/api/v1/alerts/sim-alert/escalation/pause");
    expect(JSON.parse(fetch.mock.calls[0][1].body)).toEqual({ expectedVersion: 9, reasonCode: "ManualCoordination" });
    expect(fetch.mock.calls[0][1].headers["Idempotency-Key"]).toBe("retained-pause-key");
    expect(JSON.parse(fetch.mock.calls[1][1].body)).toEqual({ expectedVersion: 9, reasonCode: "ReadyToResume" });
    expect(fetch.mock.calls[1][1].headers["Idempotency-Key"]).toBe("retained-resume-key");
    expect(fetch.mock.calls[1][1].credentials).toBe("include");
  } finally { vi.unstubAllGlobals(); }
});

test("failed read retains this alert's durable state but changing alert identity clears it and pending actions", async () => {
  vi.mocked(api.getAlertLive).mockResolvedValue(escalationLive());
  vi.mocked(api.pauseEscalation).mockRejectedValue(new TypeError("lost"));
  const { rerender } = render(<LiveAlert alertId="sim-alert" pollMs={0} />);
  fireEvent.click(await screen.findByRole("button", { name: "Pause escalation" }));
  await screen.findByRole("button", { name: "Retry Pause request" });
  vi.mocked(api.getAlertLive).mockRejectedValue(new TypeError("offline"));
  fireEvent.click(screen.getByRole("button", { name: "Refresh status" }));
  expect(await screen.findByText(/Showing the last successful response/)).toBeVisible();
  expect(screen.getByText(/Policy: DEMO-1/)).toBeVisible();
  rerender(<LiveAlert alertId="sim-other" pollMs={0} />);
  await waitFor(() => expect(api.getAlertLive).toHaveBeenCalledWith("sim-other"));
  expect(screen.queryByText(/Policy: DEMO-1/)).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /Retry Pause|Pause escalation/ })).not.toBeInTheDocument();
  expect(api.pauseEscalation).toHaveBeenCalledTimes(1);
});

test("past deadline and polling never issue commands; polling stops on unmount", async () => {
  vi.useFakeTimers();
  const live = escalationLive();
  live.escalation.nextEvaluationAtUtc = "2020-01-01T00:00:00Z";
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  const { unmount } = render(<LiveAlert alertId="sim-alert" />);
  await act(async () => {});
  await act(() => vi.advanceTimersByTimeAsync(15000));
  expect(api.getAlertLive).toHaveBeenCalledTimes(4);
  expect(api.pauseEscalation).not.toHaveBeenCalled();
  expect(api.resumeEscalation).not.toHaveBeenCalled();
  unmount();
  await act(() => vi.advanceTimersByTimeAsync(15000));
  expect(api.getAlertLive).toHaveBeenCalledTimes(4);
});

test("exhaustion, queued delivery, provenance and acknowledgement are distinct; reader has no controls", async () => {
  const live = escalationLive();
  live.manualFallbackRequired = true;
  live.canCancel = false;
  live.escalation = { ...live.escalation, runState: "Completed", exhausted: true, nextStepSequence: null, nextEvaluationAtUtc: null, terminalOutcome: "Exhausted", escalationOutboxState: "Pending", manualFallbackRequired: true, canPause: false, timeline: [{ eventId: "event", runId: "sim-run", alertVersion: 9, policyId: "sim-policy", policyVersion: "DEMO-1", planRevision: "sim-revision", stepSequence: 1, eventType: "RecipientActivated", occurredAtUtc: live.refreshedAtUtc, recipientSelectionId: "selection", failureCategory: null, overrideReason: null }] };
  live.recipients = [{ practitionerId: "backup", simulationCode: "SIM-PRAC-3", displayName: "Jules Martin", specialty: "Surgery", onCallSnapshot: null, acknowledgedAtUtc: live.refreshedAtUtc, terminalDisposition: null, responsibilityAcceptedAtUtc: null, callUnitRequestedAtUtc: null, lastResponseReasonCode: null, attempts: [], selections: [{ selectionId: "selection", practitionerRoleId: "role", channel: "SecureMessage", selectionSource: "EscalationPolicy", selectedAtUtc: live.refreshedAtUtc, escalationRunId: "sim-run", escalationStepSequence: 1, escalationPolicyId: "sim-policy", escalationPolicyVersion: "DEMO-1", escalationPlanRevision: "sim-revision" }] }];
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  render(<LiveAlert alertId="sim-alert" pollMs={0} />);
  expect(await screen.findByText(/All approved automatic steps have been queued/)).toBeVisible();
  expect(screen.getByText("Escalation delivery queue: Pending")).toBeVisible();
  expect(screen.getByText(/Added by confirmed DEMO escalation policy DEMO-1, step 1/)).toBeVisible();
  expect(screen.getByText("Responsibility accepted: Not recorded")).toBeVisible();
  expect(screen.getByRole("heading", { name: "Manual fallback required" })).toBeVisible();
  expect(screen.queryByRole("button", { name: /Pause escalation|Resume escalation/ })).not.toBeInTheDocument();
});

test("legacy alerts explicitly show automatic escalation ineligibility", async () => {
  const live = escalationLive();
  live.escalation = { ...live.escalation, automaticEscalationEligible: false, policyId: null, policyVersion: null, runId: null, runState: null, canPause: false };
  vi.mocked(api.getAlertLive).mockResolvedValue(live);
  render(<LiveAlert alertId="sim-alert" pollMs={0} />);
  expect(await screen.findByText(/Automatic escalation is unavailable.*exact confirmed plan/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "Pause escalation" })).not.toBeInTheDocument();
});
