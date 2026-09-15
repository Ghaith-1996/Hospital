import type { AlertLive } from "../../lib/alerts";

export function escalationLive(): AlertLive {
  return {
    alertId: "sim-alert", confirmedVersion: 9, alertState: "Active", outboxState: "Processed",
    refreshedAtUtc: "2026-09-14T12:00:00Z", canResolve: false, canCancel: true, manualFallbackRequired: false,
    recipients: [],
    escalation: {
      simulationOnly: true, timingAuthority: "PostgreSQLUtc", automaticEscalationEligible: true,
      policyId: "sim-policy", policyVersion: "DEMO-1", planRevision: "sim-revision", runId: "sim-run",
      runState: "Scheduled", currentStep: 1, nextStepSequence: 1, totalSteps: 1,
      nextEvaluationAtUtc: "2026-09-14T12:01:00Z", remainingPauseSeconds: null,
      paused: false, stopped: false, exhausted: false, terminalOutcome: null, reasonCode: null, failureCategory: null,
      escalationOutboxState: "NotCreated", escalationOutboxFailureCategory: null,
      manualFallbackRequired: false, canPause: true, canResume: false, timeline: [],
    },
  };
}
