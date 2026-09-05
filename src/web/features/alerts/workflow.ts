import type { AlertRecord } from "./types";

// Local prototype rules only; production lifecycle policy requires hospital approval.
export function canRespondToAlert(alert: AlertRecord): boolean {
  return alert.status === "sent" || alert.status === "in-progress" || alert.status === "escalating";
}
