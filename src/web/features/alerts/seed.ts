import type { AlertRecord, Clinician, PrototypeState, PrototypeUser } from "./types";
import { DEMO_NOW, PROTOTYPE_SCHEMA_VERSION, STORAGE_KEY } from "./types";

const users: PrototypeUser[] = [
  {
    id: "user-sophie",
    displayName: "Sophie Bernard",
    role: "operator",
    title: "Simulation Operator",
    initials: "SB",
  },
  {
    id: "user-marc",
    displayName: "Dr. Marc Tremblay",
    role: "doctor",
    title: "Simulation Emergency Physician",
    initials: "MT",
    clinicianId: "clinician-marc",
  },
  {
    id: "user-julie",
    displayName: "Dr. Julie Martin",
    role: "doctor",
    title: "Simulation Cardiology Consultant",
    initials: "JM",
    clinicianId: "clinician-julie",
  },
  {
    id: "user-david",
    displayName: "Dr. David Nguyen",
    role: "doctor",
    title: "Simulation Internal Medicine Consultant",
    initials: "DN",
    clinicianId: "clinician-david",
  },
];

const clinicians: Clinician[] = [
  {
    id: "clinician-marc",
    displayName: "Dr. Marc Tremblay",
    initials: "MT",
    specialty: "Simulation Emergency Medicine",
    department: "Fictional Emergency",
    site: "North Wing Simulation Site",
  },
  {
    id: "clinician-julie",
    displayName: "Dr. Julie Martin",
    initials: "JM",
    specialty: "Simulation Cardiology",
    department: "Fictional Cardiology",
    site: "North Wing Simulation Site",
  },
  {
    id: "clinician-david",
    displayName: "Dr. David Nguyen",
    initials: "DN",
    specialty: "Simulation Internal Medicine",
    department: "Fictional Internal Medicine",
    site: "South Wing Simulation Site",
  },
];

const alerts: AlertRecord[] = [
  {
    id: "alert-critical-1",
    label: "SIMULATION: fictional chest pain and shortness of breath.",
    patientReference: "SIM-PAT-01578",
    location: "Fictional ER - Simulation Bed 12",
    department: "Fictional Emergency",
    urgency: "critical",
    caseDetails:
      "SIMULATION: fictional 66-year-old male with chest pain and shortness of breath for 30 minutes.\nBP 170/94, HR 128, SpO2 86% on 2L O2.\nReceived ASA 325 mg and NTG x1 with no relief.\nPast history: fictional hypertension and type 2 diabetes.\nNeed cardiology evaluation and possible cath lab activation.",
    status: "sent",
    deliveryState: "not-observed",
    createdByUserId: "user-sophie",
    createdAt: "2026-08-30T14:05:00.000Z",
    updatedAt: "2026-08-30T14:11:00.000Z",
    recipients: [
      {
        clinicianId: "clinician-marc",
        response: "none",
      },
      {
        clinicianId: "clinician-julie",
        response: "none",
      },
      {
        clinicianId: "clinician-david",
        response: "none",
      },
    ],
    activities: [
      {
        id: "activity-alert-critical-1-sent",
        kind: "sent",
        label: "SIMULATION: fictional alert sent to 3 fictional clinicians.",
        occurredAt: "2026-08-30T14:11:00.000Z",
        tone: "info",
      },
      {
        id: "activity-alert-critical-1-created",
        kind: "created",
        label: "SIMULATION: fictional operator draft created.",
        occurredAt: "2026-08-30T14:05:00.000Z",
        tone: "neutral",
      },
    ],
  },
  {
    id: "alert-draft-1",
    label: "SIMULATION: fictional neurology consult draft awaiting confirmation.",
    patientReference: "SIM-PAT-1002",
    location: "East Wing / Simulation Room 4",
    department: "Fictional Neurology",
    urgency: "high",
    caseDetails: "SIMULATION: fictional neurology consult draft awaiting confirmation.",
    status: "draft",
    deliveryState: "not-applicable",
    createdByUserId: "user-sophie",
    createdAt: "2026-08-30T13:58:00.000Z",
    updatedAt: "2026-08-30T14:08:00.000Z",
    recipients: [
      {
        clinicianId: "clinician-julie",
        response: "none",
      },
    ],
    activities: [
      {
        id: "activity-alert-draft-1-created",
        kind: "created",
        label: "SIMULATION: fictional draft saved for later confirmation.",
        occurredAt: "2026-08-30T13:58:00.000Z",
        tone: "neutral",
      },
    ],
  },
  {
    id: "alert-in-progress-1",
    label: "SIMULATION: fictional sepsis follow-up accepted by Dr. Marc Tremblay.",
    patientReference: "SIM-PAT-1003",
    location: "South Wing / Simulation Room 8",
    department: "Fictional Emergency",
    urgency: "critical",
    caseDetails: "SIMULATION: fictional sepsis follow-up accepted by Dr. Marc Tremblay.",
    status: "in-progress",
    deliveryState: "delivered",
    createdByUserId: "user-sophie",
    createdAt: "2026-08-30T13:42:00.000Z",
    updatedAt: "2026-08-30T14:16:00.000Z",
    receivedAt: "2026-08-30T13:50:00.000Z",
    recipients: [
      {
        clinicianId: "clinician-marc",
        response: "accepted",
        acknowledgedAt: "2026-08-30T13:49:00.000Z",
        responsibilityAcceptedAt: "2026-08-30T13:50:00.000Z",
        respondedAt: "2026-08-30T13:50:00.000Z",
        note: "SIMULATION: fictional acceptance received.",
      },
    ],
    activities: [
      {
        id: "activity-alert-in-progress-1-accepted",
        kind: "accepted",
        label: "SIMULATION: fictional responsibility accepted by Dr. Marc Tremblay.",
        occurredAt: "2026-08-30T13:50:00.000Z",
        tone: "success",
      },
      {
        id: "activity-alert-in-progress-1-sent",
        kind: "sent",
        label: "SIMULATION: fictional alert sent to Dr. Marc Tremblay.",
        occurredAt: "2026-08-30T13:45:00.000Z",
        tone: "info",
      },
      {
        id: "activity-alert-in-progress-1-created",
        kind: "created",
        label: "SIMULATION: fictional operator draft created.",
        occurredAt: "2026-08-30T13:42:00.000Z",
        tone: "neutral",
      },
    ],
  },
  {
    id: "alert-resolved-1",
    label: "SIMULATION: fictional post-op hypotension resolved for handoff review.",
    patientReference: "SIM-PAT-1004",
    location: "West Wing / Simulation Room 2",
    department: "Fictional Internal Medicine",
    urgency: "routine",
    caseDetails: "SIMULATION: fictional post-op hypotension resolved for handoff review.",
    status: "resolved",
    deliveryState: "delivered",
    createdByUserId: "user-sophie",
    createdAt: "2026-08-30T12:35:00.000Z",
    updatedAt: "2026-08-30T13:25:00.000Z",
    receivedAt: "2026-08-30T12:39:00.000Z",
    recipients: [
      {
        clinicianId: "clinician-david",
        response: "accepted",
        acknowledgedAt: "2026-08-30T12:40:00.000Z",
        responsibilityAcceptedAt: "2026-08-30T12:42:00.000Z",
        respondedAt: "2026-08-30T12:42:00.000Z",
        note: "SIMULATION: fictional review complete.",
      },
    ],
    activities: [
      {
        id: "activity-alert-resolved-1-accepted",
        kind: "accepted",
        label: "SIMULATION: fictional responsibility accepted by Dr. David Nguyen.",
        occurredAt: "2026-08-30T12:42:00.000Z",
        tone: "success",
      },
      {
        id: "activity-alert-resolved-1-acknowledged",
        kind: "acknowledged",
        label: "SIMULATION: fictional acknowledgement received from Dr. David Nguyen.",
        occurredAt: "2026-08-30T12:40:00.000Z",
        tone: "info",
      },
      {
        id: "activity-alert-resolved-1-sent",
        kind: "sent",
        label: "SIMULATION: fictional alert sent to Dr. David Nguyen.",
        occurredAt: "2026-08-30T12:37:00.000Z",
        tone: "info",
      },
      {
        id: "activity-alert-resolved-1-created",
        kind: "created",
        label: "SIMULATION: fictional operator draft created.",
        occurredAt: "2026-08-30T12:35:00.000Z",
        tone: "neutral",
      },
    ],
  },
  {
    id: "alert-escalating-1",
    label: "SIMULATION: fictional stroke escalation moving through backup coverage.",
    patientReference: "SIM-PAT-1005",
    location: "North Wing / Simulation Room 21",
    department: "Fictional Emergency",
    urgency: "critical",
    caseDetails: "SIMULATION: fictional stroke escalation moving through backup coverage.",
    status: "escalating",
    deliveryState: "submitted",
    createdByUserId: "user-sophie",
    createdAt: "2026-08-30T13:15:00.000Z",
    updatedAt: "2026-08-30T14:20:00.000Z",
    recipients: [
      {
        clinicianId: "clinician-marc",
        response: "unavailable",
        respondedAt: "2026-08-30T13:37:00.000Z",
        note: "SIMULATION: fictional unavailable response recorded.",
      },
      {
        clinicianId: "clinician-julie",
        response: "none",
      },
    ],
    activities: [
      {
        id: "activity-alert-escalating-1-escalated",
        kind: "escalated",
        label: "SIMULATION: fictional escalation advanced to backup coverage.",
        occurredAt: "2026-08-30T14:20:00.000Z",
        tone: "critical",
      },
      {
        id: "activity-alert-escalating-1-unavailable",
        kind: "unavailable",
        label: "SIMULATION: fictional unavailable response received from Dr. Marc Tremblay.",
        occurredAt: "2026-08-30T13:37:00.000Z",
        tone: "critical",
      },
      {
        id: "activity-alert-escalating-1-created",
        kind: "created",
        label: "SIMULATION: fictional operator draft created.",
        occurredAt: "2026-08-30T13:15:00.000Z",
        tone: "neutral",
      },
    ],
    escalationSteps: [
      {
        id: "step-1",
        label: "Primary notification",
        detail: "SIMULATION: fictional primary emergency physician paged.",
        atLabel: "13:16",
        state: "complete",
      },
      {
        id: "step-2",
        label: "Backup escalation",
        detail: "SIMULATION: fictional cardiology backup notified after unavailable response.",
        atLabel: "13:40",
        state: "active",
      },
      {
        id: "step-3",
        label: "Supervisor review",
        detail: "SIMULATION: fictional supervisor review queued if backup does not respond.",
        atLabel: "13:55",
        state: "pending",
      },
    ],
  },
];

export { DEMO_NOW, PROTOTYPE_SCHEMA_VERSION, STORAGE_KEY };

export function createSeedState(): PrototypeState {
  return {
    schemaVersion: PROTOTYPE_SCHEMA_VERSION,
    selectedUserId: "user-sophie",
    users: structuredClone(users),
    clinicians: structuredClone(clinicians),
    alerts: structuredClone(alerts),
  };
}
