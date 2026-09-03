export const PROTOTYPE_SCHEMA_VERSION = 1 as const;
export const STORAGE_KEY = "critical-alerts.prototype.v1";
export const DEMO_NOW = "2026-08-30T14:24:00.000Z";

export type UserRole = "operator" | "doctor";
export type Urgency = "routine" | "high" | "critical";
export type AlertStatus = "draft" | "sent" | "in-progress" | "resolved" | "cancelled" | "escalating";
export type DeliveryState = "not-observed" | "submitted" | "delivered" | "failed" | "not-applicable";
export type DoctorResponse = "none" | "acknowledged" | "accepted" | "declined" | "unavailable";

export type PrototypeUser = {
  id: string;
  displayName: string;
  role: UserRole;
  title: string;
  initials: string;
  clinicianId?: string;
};

export type Clinician = {
  id: string;
  displayName: string;
  initials: string;
  specialty: string;
  department: string;
  site: string;
};

export type AlertRecipient = {
  clinicianId: string;
  response: DoctorResponse;
  acknowledgedAt?: string;
  responsibilityAcceptedAt?: string;
  respondedAt?: string;
  note?: string;
};

export type AlertActivity = {
  id: string;
  kind: "created" | "sent" | "acknowledged" | "accepted" | "declined" | "unavailable" | "escalated";
  label: string;
  occurredAt: string;
  tone: "neutral" | "info" | "success" | "warning" | "critical";
};

export type EscalationStep = {
  id: string;
  label: string;
  detail: string;
  atLabel: string;
  state: "complete" | "active" | "pending";
};

export type AlertRecord = {
  id: string;
  label: string;
  displayTitle?: string;
  patientReference: string;
  location: string;
  department: string;
  urgency: Urgency;
  caseDetails: string;
  status: AlertStatus;
  deliveryState: DeliveryState;
  createdByUserId: string;
  createdAt: string;
  updatedAt: string;
  receivedAt?: string;
  recipients: AlertRecipient[];
  activities: AlertActivity[];
  escalationSteps?: EscalationStep[];
};

export type PrototypeState = {
  schemaVersion: 1;
  selectedUserId: string;
  users: PrototypeUser[];
  clinicians: Clinician[];
  alerts: AlertRecord[];
};

export type AlertFilters = {
  status?: AlertStatus;
  urgency?: Urgency;
  department?: string;
  updatedAfter?: string;
};

export type DoctorInboxTab = "all" | "unread" | "in-progress" | "completed";

export type NewAlertInput = Pick<AlertRecord, "patientReference" | "location" | "department" | "urgency" | "caseDetails"> & {
  clinicianIds: string[];
};
