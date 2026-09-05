import type { AlertStatus, Urgency } from "../../features/alerts/types";

type BadgeTone = "neutral" | "info" | "success" | "warning" | "critical";

const urgencyMap: Record<Urgency, { label: string; tone: BadgeTone }> = {
  routine: { label: "Routine", tone: "neutral" },
  high: { label: "High", tone: "warning" },
  critical: { label: "Critical", tone: "critical" },
};

const statusMap: Record<AlertStatus, { label: string; tone: BadgeTone }> = {
  draft: { label: "Draft", tone: "neutral" },
  sent: { label: "Sent", tone: "success" },
  "in-progress": { label: "In Progress", tone: "warning" },
  resolved: { label: "Resolved", tone: "success" },
  cancelled: { label: "Cancelled", tone: "neutral" },
  escalating: { label: "Escalating", tone: "critical" },
};

export function StatusBadge({
  urgency,
  status,
  label,
  tone,
}: {
  urgency?: Urgency;
  status?: AlertStatus;
  label?: string;
  tone?: BadgeTone;
}) {
  const mapped = urgency ? urgencyMap[urgency] : status ? statusMap[status] : { label: label ?? "Status", tone: tone ?? "neutral" };
  const resolvedLabel = label ?? mapped.label;
  const resolvedTone = tone ?? mapped.tone;

  return (
    <span className={`status-badge status-badge--${resolvedTone}`}>
      <span className="status-badge__dot" aria-hidden="true" />
      {resolvedLabel}
    </span>
  );
}
