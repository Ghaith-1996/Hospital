"use client";
import React from "react";
import { createIdempotencyKey, isAlertApiError } from "../../lib/alerts";

// Retains an uncertain command in memory so retries cannot silently change its key or payload.
export function useIdempotentAction(afterSuccess: () => Promise<void>) {
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<unknown>(null);
  const [uncertain, setUncertain] = React.useState<string | null>(null);
  const [refreshRequired, setRefreshRequired] = React.useState(false);
  const needsRefresh = React.useRef(false);
  const attempt = React.useRef<{ label: string; key: string; run(key: string): Promise<unknown> } | null>(null);
  const lock = React.useRef(false);
  async function refresh() {
    try {
      await afterSuccess();
      needsRefresh.current = false; setRefreshRequired(false); setError(null);
    } catch {
      needsRefresh.current = true; setRefreshRequired(true);
      setError("The action succeeded, but current server status is unavailable. Reload server state before another action.");
    }
  }
  async function execute(label: string, run: (key: string) => Promise<unknown>) {
    if (lock.current || needsRefresh.current || (attempt.current && attempt.current.label !== label)) return;
    attempt.current ??= { label, key: createIdempotencyKey(), run };
    lock.current = true; setBusy(true); setError(null);
    try {
      await attempt.current.run(attempt.current.key);
      attempt.current = null; setUncertain(null);
      needsRefresh.current = true; setRefreshRequired(true);
      await refresh();
    } catch (failure) {
      setError(failure);
      if (isAlertApiError(failure) && failure.status >= 400 && failure.status < 500 && failure.status !== 429) { attempt.current = null; setUncertain(null); }
      else setUncertain(label);
    } finally { lock.current = false; setBusy(false); }
  }
  return { busy, error, uncertain, refreshRequired, refresh, execute };
}
