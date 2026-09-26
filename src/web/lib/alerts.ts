export type AlertField = {
  alertVersion: number;
  fieldId: string;
  originalValue: string;
  normalizedValue: string;
  unit: string | null;
  status: string;
  confirmedByUserId?: string;
  confirmedAtUtc?: string;
};

export type AlertRecipient = {
  practitionerId: string;
  practitionerRoleId: string | null;
  channel: string;
  selectedAtUtc: string;
  directoryRevision: string;
  directorySourceUpdatedAtUtc: string | null;
  onCallSnapshot: string | null;
  selectionSource: "Manual" | "TeamExpansion" | "EscalationPolicy";
};

export type AlertDraft = {
  alertId: string;
  state: string;
  draftVersion: number;
  simulationPatientReference: string;
  location: string;
  urgencyLabel: string;
  sourceType: string;
  sourceText: string | null;
  sbar: AlertSbar | null;
  criticalFields: AlertField[];
  approvedMessage: string | null;
  recipients: AlertRecipient[];
};

export type AlertSbar = {
  situation: string;
  background: string;
  assessment: string;
  recommendation: string;
};

export type AlertDraftInput = {
  siteId: string;
  departmentId: string;
  simulationPatientReference: string;
  location: string;
  urgencyLabel: string;
  sourceText: string;
  sbar: AlertSbar;
  criticalFields: Array<{ fieldId: string; originalValue: string; unit: string }>;
};

export type AlertDraftUpdateInput = Omit<AlertDraftInput, "siteId" | "departmentId" | "simulationPatientReference"> & {
  expectedVersion: number;
};

export type DirectoryPractitioner = {
  practitionerId: string;
  displayName: string;
  firstName: string;
  lastName: string;
  specialty: string;
  department: string | null;
  site: string | null;
  roleTitle: string | null;
  simulationCode: string;
  isActive: boolean;
  isStale: boolean;
  selectable: boolean;
  sourceSystem: string | null;
  lastSynchronizedAtUtc: string | null;
  onCallTier: string | null;
  onCallSourceSystem: string | null;
  onCallLastSynchronizedAtUtc: string | null;
  practitionerRoleId: string | null;
  availableChannels: string[];
  selectionRevision: string;
};

export type DirectorySearchParams = {
  text?: string;
  department?: string;
  site?: string;
  onCallNow?: boolean;
  includeInactive?: boolean;
};

export type AlertRecipientInput = {
  practitionerId: string;
  practitionerRoleId: string | null;
  channel: string;
  directoryRevision: string;
};

export type AlertReviewCriticalField = AlertField;

export type AlertReviewRecipient = {
  practitionerId: string;
  displayName: string;
  specialty: string;
  department: string | null;
  site: string | null;
  roleTitle: string | null;
  channel: string;
  selectedAtUtc: string;
  directorySourceUpdatedAtUtc: string | null;
  onCallSnapshot: string | null;
  isStale: boolean;
  directoryRevision: string;
  selectionSource: "Manual" | "TeamExpansion" | "EscalationPolicy";
};

export type AlertReview = {
  alertId: string;
  draftVersion: number;
  state: string;
  simulationPatientReference: string;
  location: string;
  urgencyLabel: string;
  approvedMessage: string;
  criticalFields: AlertReviewCriticalField[];
  recipients: AlertReviewRecipient[];
  demoEscalationPolicyVersion: string;
  demoNotificationPolicyVersion: string;
  escalationPlan?: EscalationPlan | null;
};

export type EscalationPlan = {
  policyId: string;
  policyVersion: string;
  revision: string;
  steps: {
    stepId: string;
    sequenceNumber: number;
    delaySeconds: number;
    recipients: {
      practitionerId: string;
      practitionerRoleId: string | null;
      displayName: string;
      specialty: string;
      department: string | null;
      site: string | null;
      roleTitle: string | null;
      channel: string;
      directoryRevision: string;
      directorySourceUpdatedAtUtc: string | null;
      onCallSnapshot: string | null;
      onCallEvidence?: {
        assignmentId: string;
        sourceSystem: string;
        sourceRecordId: string;
        startsAtUtc: string;
        endsAtUtc: string;
        lastSynchronizedAtUtc: string;
      } | null;
    }[];
  }[];
};

export type ConfirmResult = {
  alertId: string;
  confirmedVersion: number;
  state: "DispatchQueued";
  replayed: boolean;
};

export type MyAlertSummary = {
  alertId: string;
  confirmedVersion: number;
  state: string;
  location: string;
  urgencyLabel: string;
  confirmedAtUtc: string;
  channels: string[];
  openedState: string;
  acknowledgedAtUtc: string | null;
  terminalDisposition: string | null;
  responsibilityAcceptedAtUtc: string | null;
  callUnitRequestedAtUtc: string | null;
  lastResponseReasonCode: string | null;
};

export type MyAlertCriticalField = {
  fieldId: string;
  value: string;
  unit: string | null;
};

export type MyAlertDetail = Omit<MyAlertSummary, "confirmedAtUtc"> & {
  simulationPatientReference: string;
  approvedMessage: string;
  criticalFields: MyAlertCriticalField[];
  secureMessageOpenedAtUtc: string | null;
};

export type OpenedRecipientAlertResult = {
  alertId: string;
  confirmedVersion: number;
  secureMessageOpenedAtUtc: string | null;
  replayed: boolean;
};

export type RecipientResponseResult = {
  alertId: string;
  confirmedVersion: number;
  responseType: string;
  acknowledgedAtUtc: string | null;
  terminalDisposition: string | null;
  responsibilityAcceptedAtUtc: string | null;
  callUnitRequestedAtUtc: string | null;
  reasonCode: string | null;
  replayed: boolean;
};

export type RecipientResponseType = "Acknowledged" | "Accepted" | "Declined" | "Unavailable" | "CallUnitRequested";

export type RecipientResponseReasonCode =
  | "simulation-acknowledged"
  | "simulation-responsibility-accepted"
  | "simulation-declined"
  | "simulation-not-my-service"
  | "simulation-wrong-specialty"
  | "simulation-not-available"
  | "simulation-unavailable"
  | "simulation-no-coverage"
  | "simulation-not-on-call"
  | "simulation-call-unit-requested";

export type AlertLiveAttempt = {
  channel: string;
  attemptNumber: number;
  status: string;
  openedState: string;
  openedAtUtc: string | null;
  requestedAtUtc: string;
  submittedAtUtc: string | null;
  deliveredAtUtc: string | null;
  failedAtUtc: string | null;
  failureCategory: string | null;
};

export type AlertLiveRecipient = {
  practitionerId: string;
  simulationCode: string;
  displayName: string;
  specialty: string;
  onCallSnapshot: string | null;
  acknowledgedAtUtc: string | null;
  terminalDisposition: string | null;
  responsibilityAcceptedAtUtc: string | null;
  callUnitRequestedAtUtc: string | null;
  lastResponseReasonCode: string | null;
  attempts: AlertLiveAttempt[];
  selectionSources?: string[] | null;
};

export type AlertLiveEscalation = {
  policyId: string; policyVersion: string; state: string; currentStep: number;
  nextDueAtUtc: string | null; remainingDelaySeconds: number | null; stopReason: string | null;
  canPause: boolean; canResume: boolean;
  events: { sequence: number; kind: string; step: number; occurredAtUtc: string; recipientSelectionId: string | null; actorUserId: string | null }[];
};

export type AlertLive = {
  operationalWarnings?: OperationalWarning[];
  alertId: string;
  confirmedVersion: number;
  alertState: string;
  outboxState: string;
  refreshedAtUtc: string;
  canResolve: boolean;
  canCancel: boolean;
  manualFallbackRequired: boolean;
  recipients: AlertLiveRecipient[];
  escalation?: AlertLiveEscalation | null;
};

const liveAlertStates = ["PendingConfirmation", "DispatchQueued", "Active", "Resolved", "Cancelled", "Failed"] as const;
const liveOutboxStates = ["NotCreated", "Pending", "Processing", "Processed", "Failed"] as const;
const liveChannels = ["SecureMessage", "Sms", "Voice"] as const;
const liveAttemptStatuses = ["Requested", "Submitted", "Delivered", "Failed"] as const;
const liveObservationStates = ["PendingNotObserved", "Occurred", "Failed", "NotApplicable"] as const;
const liveSelectionSources = ["Manual", "TeamExpansion", "EscalationPolicy"] as const;
const liveTerminalDispositions = ["Accepted", "Declined", "Unavailable"] as const;
const liveResponseReasons = [
  "simulation-acknowledged", "simulation-responsibility-accepted", "simulation-declined",
  "simulation-not-my-service", "simulation-wrong-specialty", "simulation-not-available",
  "simulation-unavailable", "simulation-no-coverage", "simulation-not-on-call",
  "simulation-call-unit-requested",
] as const;
const liveFailureCategories = [
  "provider-unavailable", "provider-failed", "provider-no-result", "sms-failure", "voice-no-answer",
  "simulation-provider-rejected", "simulation-provider-outage", "delivery-failed", "delivery-pending",
  "delivery-retry", "practitioner-missing", "practitioner-inactive", "role-invalid", "channel-not-allowed",
  "channel-unavailable", "endpoint-unavailable", "dispatch-validation", "domain-validation", "worker-error",
] as const;
const liveEscalationStates = ["AwaitingActivation", "Scheduled", "Running", "Completed", "Stopped", "Paused", "Exhausted", "Failed"] as const;
const liveEscalationEvents = [
  "Scheduled", "StepDue", "RecipientActivated", "DispatchQueued", "Paused", "Resumed",
  "StoppedByResponsibility", "StoppedByResolution", "StoppedByCancellation", "Exhausted", "ProcessingFailed",
] as const;
const liveWarningCodes = [
  "ProviderUnavailable", "DispatchDelayed", "DeliveryFailed", "DirectoryStale",
  "DirectorySynchronizationFailed", "DatabaseUnavailable", "EscalationProcessingDelayed", "EscalationExhausted",
] as const;

type LiveRecord = Record<string, unknown>;

function liveRecord(value: unknown): value is LiveRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function liveString(value: unknown): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= 4096;
}

function liveEnum<T extends string>(value: unknown, choices: readonly T[]): T | null {
  return typeof value === "string" && (choices as readonly string[]).includes(value) ? value as T : null;
}

function liveNullableString(value: unknown): value is string | null {
  return value === null || liveString(value);
}

function liveDate(value: unknown): value is string {
  return liveString(value) && Number.isFinite(Date.parse(value));
}

function liveNullableDate(value: unknown): value is string | null {
  return value === null || liveDate(value);
}

function liveNumber(value: unknown, minimum = 0): value is number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= minimum;
}

function liveBoolean(value: unknown): value is boolean {
  return typeof value === "boolean";
}

function invalidLiveProjection(): never {
  throw new Error("The live status response is invalid. Refresh status and try again.");
}

function decodeLiveAttempt(value: unknown): AlertLiveAttempt {
  if (!liveRecord(value)) return invalidLiveProjection();
  const channel = liveEnum(value.channel, liveChannels);
  const status = liveEnum(value.status, liveAttemptStatuses);
  const openedState = liveEnum(value.openedState, liveObservationStates);
  const failureCategory = value.failureCategory === null ? null : liveEnum(value.failureCategory, liveFailureCategories);
  if (!channel || !status || !openedState || failureCategory === null && value.failureCategory !== null
      || !liveNumber(value.attemptNumber, 1) || !liveDate(value.requestedAtUtc)
      || !liveNullableDate(value.openedAtUtc) || !liveNullableDate(value.submittedAtUtc)
      || !liveNullableDate(value.deliveredAtUtc) || !liveNullableDate(value.failedAtUtc)) return invalidLiveProjection();
  return { channel, attemptNumber: value.attemptNumber, status, openedState, openedAtUtc: value.openedAtUtc,
    requestedAtUtc: value.requestedAtUtc, submittedAtUtc: value.submittedAtUtc, deliveredAtUtc: value.deliveredAtUtc,
    failedAtUtc: value.failedAtUtc, failureCategory };
}

function decodeLiveRecipient(value: unknown): AlertLiveRecipient {
  if (!liveRecord(value)) return invalidLiveProjection();
  const terminalDisposition = value.terminalDisposition === null ? null : liveEnum(value.terminalDisposition, liveTerminalDispositions);
  const reasonCode = value.lastResponseReasonCode === null ? null : liveEnum(value.lastResponseReasonCode, liveResponseReasons);
  const selectionSources = value.selectionSources === null ? null
    : Array.isArray(value.selectionSources) ? value.selectionSources.map(source => liveEnum(source, liveSelectionSources)) : null;
  if (!liveString(value.practitionerId) || !liveString(value.simulationCode) || !liveString(value.displayName)
      || !liveString(value.specialty) || !liveNullableString(value.onCallSnapshot)
      || !liveNullableDate(value.acknowledgedAtUtc) || !liveNullableDate(value.responsibilityAcceptedAtUtc)
      || !liveNullableDate(value.callUnitRequestedAtUtc)
      || terminalDisposition === null && value.terminalDisposition !== null
      || reasonCode === null && value.lastResponseReasonCode !== null
      || !Array.isArray(value.attempts) || !value.attempts.every(attempt => liveRecord(attempt))
      || value.selectionSources !== null && (!Array.isArray(selectionSources) || selectionSources.some(source => source === null))) return invalidLiveProjection();
  return { practitionerId: value.practitionerId, simulationCode: value.simulationCode, displayName: value.displayName,
    specialty: value.specialty, onCallSnapshot: value.onCallSnapshot, acknowledgedAtUtc: value.acknowledgedAtUtc,
    terminalDisposition, responsibilityAcceptedAtUtc: value.responsibilityAcceptedAtUtc,
    callUnitRequestedAtUtc: value.callUnitRequestedAtUtc, lastResponseReasonCode: reasonCode,
    attempts: value.attempts.map(decodeLiveAttempt), selectionSources: selectionSources as AlertLiveRecipient["selectionSources"] };
}

function decodeLiveEscalation(value: unknown): AlertLiveEscalation | null {
  if (value === null) return null;
  if (!liveRecord(value)) return invalidLiveProjection();
  const state = liveEnum(value.state, liveEscalationStates);
  const stopReason = value.stopReason === null ? null : liveEnum(value.stopReason, liveEscalationEvents);
  if (!liveString(value.policyId) || !liveString(value.policyVersion) || !state || !liveNumber(value.currentStep)
      || !(value.nextDueAtUtc === null || liveDate(value.nextDueAtUtc))
      || !(value.remainingDelaySeconds === null || typeof value.remainingDelaySeconds === "number"
        && Number.isFinite(value.remainingDelaySeconds) && value.remainingDelaySeconds >= 0)
      || stopReason === null && value.stopReason !== null || !liveBoolean(value.canPause) || !liveBoolean(value.canResume)
      || !Array.isArray(value.events)) return invalidLiveProjection();
  const events = value.events.map(event => {
    if (!liveRecord(event)) return invalidLiveProjection();
    const kind = liveEnum(event.kind, liveEscalationEvents);
    if (!kind || !liveNumber(event.sequence, 1) || !liveNumber(event.step)
        || !liveDate(event.occurredAtUtc) || !liveNullableString(event.recipientSelectionId)
        || !liveNullableString(event.actorUserId)) return invalidLiveProjection();
    return { sequence: event.sequence as number, kind, step: event.step as number, occurredAtUtc: event.occurredAtUtc,
      recipientSelectionId: event.recipientSelectionId as string | null, actorUserId: event.actorUserId as string | null };
  });
  return { policyId: value.policyId, policyVersion: value.policyVersion, state, currentStep: value.currentStep,
    nextDueAtUtc: value.nextDueAtUtc, remainingDelaySeconds: value.remainingDelaySeconds, stopReason,
    canPause: value.canPause, canResume: value.canResume, events };
}

function decodeLiveWarning(value: unknown): OperationalWarning {
  if (!liveRecord(value)) return invalidLiveProjection();
  const code = liveEnum(value.code, liveWarningCodes);
  if (!code || !liveString(value.title) || !liveString(value.explanation)
      || !liveString(value.recommendedApplicationAction) || !liveBoolean(value.requiresHospitalFallback)) return invalidLiveProjection();
  return { code, title: value.title, explanation: value.explanation,
    recommendedApplicationAction: value.recommendedApplicationAction, requiresHospitalFallback: value.requiresHospitalFallback };
}

export function decodeAlertLive(value: unknown, expectedAlertId: string): AlertLive {
  if (!liveRecord(value) || !liveString(value.alertId) || !liveString(expectedAlertId)
      || value.alertId.toLowerCase() !== expectedAlertId.toLowerCase() || !liveNumber(value.confirmedVersion, 1)
      || !liveEnum(value.alertState, liveAlertStates) || !liveEnum(value.outboxState, liveOutboxStates)
      || !liveDate(value.refreshedAtUtc) || !liveBoolean(value.canResolve) || !liveBoolean(value.canCancel)
      || !liveBoolean(value.manualFallbackRequired) || !Array.isArray(value.recipients)
      || !Object.hasOwn(value, "escalation") || !Array.isArray(value.operationalWarnings)) return invalidLiveProjection();
  return { alertId: value.alertId, confirmedVersion: value.confirmedVersion,
    alertState: value.alertState as AlertLive["alertState"], outboxState: value.outboxState as AlertLive["outboxState"],
    refreshedAtUtc: value.refreshedAtUtc, canResolve: value.canResolve, canCancel: value.canCancel,
    manualFallbackRequired: value.manualFallbackRequired, recipients: value.recipients.map(decodeLiveRecipient),
    escalation: decodeLiveEscalation(value.escalation), operationalWarnings: value.operationalWarnings.map(decodeLiveWarning) };
}

export type AlertLifecycleResult = {
  alertId: string;
  confirmedVersion: number;
  state: string;
  replayed: boolean;
};

type ProblemDetails = {
  detail?: string;
  title?: string;
  errors?: Record<string, string[]>;
};

export class AlertApiError extends Error {
  readonly status: number;
  readonly code: string | null;

  constructor(status: number, code: string | null, message: string) {
    super(message);
    this.name = "AlertApiError";
    this.status = status;
    this.code = code;
  }
}

export async function requestJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(path, {
    ...init,
    credentials: "include",
    cache: "no-store",
    headers: {
      ...(init.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
      ...(init.headers ?? {}),
    },
  });
  if (response.ok) {
    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  }

  let problem: ProblemDetails = {};
  try {
    problem = (await response.json()) as ProblemDetails;
  } catch {
    // Keep the browser-facing error generic when the server did not return problem details.
  }
  const code = problem.detail ?? Object.keys(problem.errors ?? {})[0] ?? null;
  throw new AlertApiError(response.status, code, problem.title ?? "The request could not be completed.");
}

export function isAlertApiError(error: unknown): error is AlertApiError {
  return error instanceof AlertApiError;
}

export function createAlertDraft(input: AlertDraftInput): Promise<AlertDraft> {
  return requestJson<AlertDraft>("/api/v1/alerts/drafts", {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function getAlertDraft(alertId: string): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}`);
}

export function updateAlertDraft(alertId: string, input: AlertDraftUpdateInput): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}`, {
    method: "PATCH",
    body: JSON.stringify(input),
  });
}

export function confirmCriticalField(
  alertId: string,
  input: {
    expectedVersion: number;
    fieldId: string;
    originalValue: string;
    normalizedValue: string;
    unit: string | null;
  },
): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}/field-confirmations`, {
    method: "POST",
    body: JSON.stringify(input),
  });
}

export function submitAlertDraft(alertId: string, expectedVersion: number): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}/submit-for-confirmation`, {
    method: "POST",
    body: JSON.stringify({ expectedVersion }),
  });
}

export function setApprovedMessage(alertId: string, expectedVersion: number, approvedMessage: string): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}/approved-message`, {
    method: "PUT",
    body: JSON.stringify({ expectedVersion, approvedMessage }),
  });
}

export function replaceAlertRecipients(
  alertId: string,
  expectedVersion: number,
  recipients: AlertRecipientInput[],
): Promise<AlertDraft> {
  return requestJson<AlertDraft>(`/api/v1/alerts/${alertId}/recipients`, {
    method: "PUT",
    body: JSON.stringify({ expectedVersion, recipients }),
  });
}

export function searchDirectory(params: DirectorySearchParams = {}): Promise<DirectoryPractitioner[]> {
  const query = new URLSearchParams();
  if (params.text?.trim()) query.set("q", params.text.trim());
  if (params.department?.trim()) query.set("department", params.department.trim());
  if (params.site?.trim()) query.set("site", params.site.trim());
  if (params.onCallNow !== undefined) query.set("onCallNow", String(params.onCallNow));
  query.set("includeInactive", String(params.includeInactive ?? false));
  const suffix = query.toString();
  return requestJson<DirectoryPractitioner[]>(`/api/v1/directory/practitioners${suffix ? `?${suffix}` : ""}`);
}

export function getAlertReview(alertId: string): Promise<AlertReview> {
  return requestJson<AlertReview>(`/api/v1/alerts/${alertId}/review`);
}

export function createIdempotencyKey(): string {
  const randomUuid = globalThis.crypto?.randomUUID?.();
  return `phase6-${randomUuid ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`}`;
}

export function confirmAlertReview(alertId: string, expectedVersion: number, idempotencyKey: string, plan: EscalationPlan): Promise<ConfirmResult> {
  return requestJson<ConfirmResult>(`/api/v1/alerts/${alertId}/confirm`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion, escalationPolicyId: plan.policyId, escalationPolicyVersion: plan.policyVersion, escalationPlanRevision: plan.revision }),
  });
}

export function getMyAlerts(): Promise<MyAlertSummary[]> {
  return requestJson<MyAlertSummary[]>("/api/v1/my-alerts");
}

export function getMyAlert(alertId: string): Promise<MyAlertDetail> {
  return requestJson<MyAlertDetail>(`/api/v1/my-alerts/${alertId}`);
}

export function markMyAlertOpened(
  alertId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<OpenedRecipientAlertResult> {
  return requestJson<OpenedRecipientAlertResult>(`/api/v1/my-alerts/${alertId}/opened`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion }),
  });
}

export function recordMyAlertResponse(
  alertId: string,
  expectedVersion: number,
  responseType: RecipientResponseType,
  idempotencyKey: string,
  reasonCode?: RecipientResponseReasonCode,
): Promise<RecipientResponseResult> {
  return requestJson<RecipientResponseResult>(`/api/v1/my-alerts/${alertId}/responses`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion, responseType, reasonCode }),
  });
}

export type OperationalWarning = { code: string; title: string; explanation: string; recommendedApplicationAction: string; requiresHospitalFallback: boolean };

export const operationalWarningMessages: Readonly<Record<string, readonly [string, string]>> = {
  ProviderUnavailable: ["Provider unavailable", "The simulated notification provider is unavailable. Refresh status. Do not create a duplicate alert. Manual fallback: REQUIRES_HOSPITAL_DECISION."],
  DispatchDelayed: ["Dispatch delayed", "The confirmed alert remains durable. Refresh status before retrying. If delay persists, follow the approved manual fallback procedure: REQUIRES_HOSPITAL_DECISION."],
  DeliveryFailed: ["Delivery failed", "Review attempts and refresh status. Delivery does not establish responsibility. Do not create a duplicate alert. Manual fallback: REQUIRES_HOSPITAL_DECISION."],
  DirectoryStale: ["Directory stale", "Selected directory information is marked stale. Review directory freshness and source evidence before another recipient action."],
  DirectorySynchronizationFailed: ["Directory synchronization failed", "Review the latest safe synchronization status and validate a new import. Directory fallback: REQUIRES_HOSPITAL_DECISION."],
  DatabaseUnavailable: ["Database unavailable", "Check readiness and refresh status before retrying. Recovery authority: REQUIRES_HOSPITAL_DECISION."],
  EscalationProcessingDelayed: ["Escalation processing delayed", "Refresh status and review the confirmed plan. Do not edit workflow state. Manual fallback: REQUIRES_HOSPITAL_DECISION."],
  EscalationExhausted: ["Escalation steps exhausted", "Review attempts and responsibility status. If responsibility remains unassigned, use the approved fallback: REQUIRES_HOSPITAL_DECISION."],
};

export function getAlertLive(alertId: string): Promise<AlertLive> {
  return requestJson<unknown>(`/api/v1/alerts/${alertId}/live`).then(value => decodeAlertLive(value, alertId));
}

export function setEscalationPaused(alertId: string, expectedVersion: number, paused: boolean, idempotencyKey: string): Promise<AlertLifecycleResult> {
  const action = paused ? "pause" : "resume";
  return requestJson<AlertLifecycleResult>(`/api/v1/alerts/${alertId}/escalation/${action}`, {
    method: "POST", headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion, reasonCode: `simulation-${action}-requested` }),
  });
}

export function resolveAlert(
  alertId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<AlertLifecycleResult> {
  return requestJson<AlertLifecycleResult>(`/api/v1/alerts/${alertId}/resolve`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion }),
  });
}

export function cancelAlert(
  alertId: string,
  expectedVersion: number,
  idempotencyKey: string,
): Promise<AlertLifecycleResult> {
  return requestJson<AlertLifecycleResult>(`/api/v1/alerts/${alertId}/cancel`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion }),
  });
}
