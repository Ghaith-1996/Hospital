import { isAlertApiError, requestJson } from "./alerts";

export const auditActions = [
  "alert.draft.created", "alert.draft.updated", "alert.critical-field.confirmed", "alert.draft.submitted",
  "alert.approved-message.updated", "alert.recipients.replaced", "alert.confirmed", "alert.resolved", "alert.cancelled",
  "recipient.opened", "recipient.response.acknowledged", "recipient.response.accepted", "recipient.response.declined",
  "recipient.response.unavailable", "recipient.response.callunitrequested", "directory.import.applied",
  "dispatch.suppressed", "dispatch.completed", "dispatch.failed", "dispatch.delivery-event", "dispatch.retry-scheduled",
  "escalation-scheduled", "escalation-recipients-activated", "escalation-dispatch-queued", "escalation-stopped",
  "escalation-exhausted", "escalation-processing-failed", "escalation.paused", "escalation.resumed", "audit.read",
];
export const auditOutcomes = ["succeeded", "failed", "scheduled", "activated", "stopped", "exhausted", "completed",
  "ResponsibilityAccepted", "Resolved", "Cancelled", "ProcessingFailed", "Exhausted", "manual-fallback",
  "ConfirmedRecipientUnavailable", "ConfirmedPlanInvalid", "ProcessingError"];
export const auditResourceTypes = ["alert", "delivery-attempt", "directory_sync_run", "EscalationRun", "audit"];
export type AuditFilters = { occurredFromUtc?: string; occurredToUtc?: string; action?: string; outcome?: string; resourceType?: string; correlationId?: string };
export type AuditEvent = { id: string; action: string; resourceType: string; resourceId: string; actorType: string;
  outcome: string; correlationId: string | null; occurredAtUtc: string; metadata: Record<string, number | boolean | string | string[]> };
export type AuditPage = { events: AuditEvent[]; nextCursor: string | null };
const uuid = /^(?:[a-f0-9]{32}|[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})$/i;
const cursorPattern = /^[a-zA-Z0-9_-]{32}$/;
const countKeys = ["version", "alertVersion", "draftVersion", "recipientCount", "attemptNumber", "retryCount", "resultCount",
  "pageSize", "inserted", "updated", "rejected", "stepSequence", "secureMessageAttemptCount"];
const channels = ["SecureMessage", "Sms", "Voice"];
const responseTypes = ["Acknowledged", "Accepted", "Declined", "Unavailable", "CallUnitRequested"];
const filterNames = ["occurredFromUtc", "occurredToUtc", "action", "outcome", "resourceType", "correlationId"];
const actors = ["user", "worker", "SimulationWorker", "system"];
function object(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === "object" && !Array.isArray(value); }
function allowed(value: unknown, values: string[]): value is string { return typeof value === "string" && (value === "unknown" || values.includes(value)); }
function opaque(value: unknown): value is string { return typeof value === "string" && uuid.test(value); }
function utc(value: unknown): value is string { return typeof value === "string" && value.length <= 40 && /(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value)); }
export class AuditClientError extends Error {}

function metadata(value: unknown): AuditEvent["metadata"] {
  const result: AuditEvent["metadata"] = {};
  if (!object(value)) return result;
  for (const [key, item] of Object.entries(value)) {
    if (countKeys.includes(key) && typeof item === "number" && Number.isInteger(item) && item >= 0 && item <= 1000000) result[key] = item;
    if (key === "simulationOnly" && typeof item === "boolean") result[key] = item;
    if (key === "channel" && typeof item === "string" && channels.includes(item)) result[key] = item;
    if (key === "responseType" && typeof item === "string" && responseTypes.includes(item)) result[key] = item;
    const vocabulary = key === "channels" ? channels : key === "filtersUsed" ? filterNames : null;
    if (vocabulary && Array.isArray(item) && item.length <= vocabulary.length && item.every(entry => typeof entry === "string" && vocabulary.includes(entry))) result[key] = item;
  }
  return result;
}
function decode(value: unknown): AuditPage {
  const invalid = () => new AuditClientError("Audit response was invalid. Retry the query.");
  if (!object(value) || !Array.isArray(value.events) || value.events.length > 100
    || !(value.nextCursor === null || typeof value.nextCursor === "string" && cursorPattern.test(value.nextCursor))) throw invalid();
  const events = value.events.map((row: unknown): AuditEvent => {
    if (!object(row) || !opaque(row.id) || !opaque(row.resourceId) || !allowed(row.action, auditActions)
      || !allowed(row.resourceType, auditResourceTypes) || !allowed(row.actorType, actors) || !allowed(row.outcome, auditOutcomes)
      || !(row.correlationId === null || opaque(row.correlationId)) || !utc(row.occurredAtUtc) || !object(row.metadata)) throw invalid();
    return { id: row.id, resourceId: row.resourceId, action: row.action, resourceType: row.resourceType, actorType: row.actorType,
      outcome: row.outcome, correlationId: row.correlationId, occurredAtUtc: row.occurredAtUtc, metadata: metadata(row.metadata) };
  });
  return { events, nextCursor: value.nextCursor };
}
export async function queryAudit(filters: AuditFilters, cursor: string | null = null): Promise<AuditPage> {
  const parameters = new URLSearchParams({ pageSize: "50" });
  for (const [key, value] of Object.entries(filters)) {
    if (!value) continue;
    const valid = key === "correlationId" ? uuid.test(value)
      : key === "action" ? auditActions.includes(value)
      : key === "outcome" ? auditOutcomes.includes(value)
      : key === "resourceType" ? auditResourceTypes.includes(value)
      : (key === "occurredFromUtc" || key === "occurredToUtc") && utc(value);
    if (!valid) throw new AuditClientError("Audit filters are invalid. Use UTC dates and an opaque correlation ID.");
    parameters.set(key, value);
  }
  if (filters.occurredFromUtc && filters.occurredToUtc && Date.parse(filters.occurredFromUtc) >= Date.parse(filters.occurredToUtc))
    throw new AuditClientError("The end time must be after the start time.");
  if (cursor !== null) {
    if (!cursorPattern.test(cursor)) throw new AuditClientError("Audit pagination was invalid. Apply filters again.");
    parameters.set("cursor", cursor);
  }
  try { return decode(await requestJson<unknown>("/api/v1/admin/audit?" + parameters.toString())); }
  catch (error) {
    if (error instanceof AuditClientError) throw error;
    const status = isAlertApiError(error) ? error.status : 0;
    throw new AuditClientError(status === 401 ? "Session unavailable. Select a backend development identity and retry."
      : status === 403 ? "Audit access requires an Auditor or SystemAdministrator identity."
      : "Audit service unavailable. Retry when the API and database are available.");
  }
}
