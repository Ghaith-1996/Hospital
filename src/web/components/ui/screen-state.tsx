import React from "react";
import { AlertIcon, ClockIcon, SearchIcon } from "./icons";

type ScreenStateKind = "loading" | "empty" | "not-found" | "recoverable-storage";

const defaultMessages: Record<ScreenStateKind, string> = {
  loading: "Loading fictional demo workspace",
  empty: "No fictional records are available yet.",
  "not-found": "The requested fictional record was not found.",
  "recoverable-storage": "Demo changes are available for this session but could not be saved in this browser.",
};

export function ScreenState({
  kind,
  label,
  description,
  action,
}: {
  kind: ScreenStateKind;
  label?: string;
  description?: string;
  action?: React.ReactNode;
}) {
  const resolvedLabel = label ?? defaultMessages[kind];
  const Icon = kind === "loading" ? ClockIcon : kind === "empty" ? SearchIcon : AlertIcon;

  return (
    <section className={`screen-state screen-state--${kind}`} role="status" aria-label={resolvedLabel}>
      <Icon className="screen-state__icon" />
      <h1>{resolvedLabel}</h1>
      {description ? <p>{description}</p> : null}
      {action ? <div className="screen-state__action">{action}</div> : null}
    </section>
  );
}
