import React from "react";
export function StatusBadge({ label, tone = "neutral" }: { label: string; tone?: "neutral" | "info" | "success" | "warning" | "critical" }) {
  return <span className={`status-badge status-badge--${tone}`}><span className="status-badge__dot" aria-hidden="true" />{label}</span>;
}
