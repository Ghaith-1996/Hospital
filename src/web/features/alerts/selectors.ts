import type { AlertFilters, AlertRecord, Clinician, DoctorInboxTab, PrototypeState, PrototypeUser } from "./types";

export function selectCurrentUser(state: PrototypeState): PrototypeUser {
  return state.users.find((user) => user.id === state.selectedUserId) ?? state.users[0];
}

export function selectAlertById(state: PrototypeState, id: string): AlertRecord | undefined {
  return state.alerts.find((alert) => alert.id === id);
}

export function selectAlerts(state: PrototypeState, filters: AlertFilters): AlertRecord[] {
  return state.alerts.filter((alert) => {
    if (filters.status && alert.status !== filters.status) return false;
    if (filters.urgency && alert.urgency !== filters.urgency) return false;
    if (filters.department && alert.department !== filters.department) return false;
    if (filters.updatedAfter && alert.updatedAt <= filters.updatedAfter) return false;
    return true;
  });
}

export function selectDoctorAlerts(state: PrototypeState, clinicianId: string, tab: DoctorInboxTab): AlertRecord[] {
  const assignedAlerts = state.alerts.filter((alert) =>
    alert.recipients.some((recipient) => recipient.clinicianId === clinicianId),
  );

  if (tab === "all") return assignedAlerts;

  if (tab === "in-progress") {
    return assignedAlerts.filter((alert) => {
      const recipient = alert.recipients.find((candidate) => candidate.clinicianId === clinicianId);
      return (
        recipient?.response === "acknowledged" ||
        recipient?.response === "accepted" ||
        alert.status === "in-progress" ||
        alert.status === "escalating"
      );
    });
  }

  if (tab === "completed") {
    return assignedAlerts.filter((alert) => {
      const recipient = alert.recipients.find((candidate) => candidate.clinicianId === clinicianId);
      return alert.status === "resolved" || recipient?.response === "declined" || recipient?.response === "unavailable";
    });
  }

  return assignedAlerts.filter((alert) =>
    alert.recipients.some((recipient) => recipient.clinicianId === clinicianId && recipient.response === "none"),
  );
}

export function formatAlertDisplayTitle(alert: AlertRecord) {
  if (alert.displayTitle) return alert.displayTitle;

  return alert.label
    .replace(/^SIMULATION:\s*/i, "")
    .replace(/^fictional\s*/i, "")
    .replace(/[.!?]\s*$/u, "");
}

export function searchClinicians(state: PrototypeState, query: string): Clinician[] {
  const normalized = query.trim().toLowerCase();
  if (!normalized) return state.clinicians;

  return state.clinicians.filter((clinician) =>
    [
      clinician.displayName,
      clinician.specialty,
      clinician.department,
      clinician.site,
      clinician.initials,
    ].some((value) => value.toLowerCase().includes(normalized)),
  );
}
