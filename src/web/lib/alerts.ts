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
  normalizedValue: string;
  unit: string | null;
};

export type MyAlertDetail = MyAlertSummary & {
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
};

export type AlertLive = {
  alertId: string;
  confirmedVersion: number;
  alertState: string;
  outboxState: string;
  refreshedAtUtc: string;
  canResolve: boolean;
  canCancel: boolean;
  manualFallbackRequired: boolean;
  recipients: AlertLiveRecipient[];
};

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

export function confirmAlertReview(alertId: string, expectedVersion: number, idempotencyKey: string): Promise<ConfirmResult> {
  return requestJson<ConfirmResult>(`/api/v1/alerts/${alertId}/confirm`, {
    method: "POST",
    headers: { "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ expectedVersion }),
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

export function getAlertLive(alertId: string): Promise<AlertLive> {
  return requestJson<AlertLive>(`/api/v1/alerts/${alertId}/live`);
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
