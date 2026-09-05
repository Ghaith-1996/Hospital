"use client";
import React from "react";
import { isAlertApiError } from "../../lib/alerts";

export function errorGuidance(error: unknown): string {
  if (!isAlertApiError(error)) return "API/network unavailable. Your changes were not confirmed. Retry when the simulation API is available.";
  if (error.status === 401) return "Session unavailable. Select a backend development identity and retry.";
  if (error.status === 403) return "You are not authorized for this action. Select an authorized simulation identity.";
  if (error.status === 404) return "This alert or practitioner no longer exists or is inaccessible to this identity.";
  if (error.code?.includes("directory") || error.code?.includes("inactive")) return "Directory information changed or a recipient is inactive. Reload and review recipients and channels again.";
  if (error.status === 409) return "The alert changed. Current server data must be reloaded and reviewed again before continuing. No stale edits were saved.";
  if (error.status === 429) return "Too many requests. Wait a minute, then retry the same action.";
  return `${error.message} Check required simulation fields and confirmed values, then retry. ${error.code ?? ""}`;
}

export function ApiError({ error, retry }: { error: unknown; retry?: () => void }) {
  return error ? <div role="alert" className="error-panel"><p>{typeof error === "string" ? error : errorGuidance(error)}</p>{retry && <button type="button" className="button-secondary" onClick={retry}>Reload server state</button>}</div> : null;
}
export function Loading() { return <p role="status">Loading server state…</p>; }

export function useServerQuery<T>(load: () => Promise<T>) {
  const [data, setData] = React.useState<T | null>(null);
  const [error, setError] = React.useState<unknown>(null);
  const [loading, setLoading] = React.useState(true);
  const sequence = React.useRef({ value: 0 });
  const refresh = React.useCallback(async () => {
    const current = ++sequence.current.value;
    try {
      const value = await load();
      if (current === sequence.current.value) { setData(value); setError(null); setLoading(false); }
    } catch (failure) {
      if (current === sequence.current.value) { setError(failure); setLoading(false); }
      throw failure;
    }
  }, [load]);
  React.useEffect(() => {
    const counter = sequence.current;
    void refresh().catch(() => {});
    return () => { ++counter.value; };
  }, [refresh]);
  const reload = React.useCallback(() => { void refresh().catch(() => {}); }, [refresh]);
  return { data, setData, error, loading, reload, refresh };
}

export function useUnsavedChanges(dirty: boolean) {
  const dirtyRef = React.useRef(dirty);
  React.useEffect(() => { dirtyRef.current = dirty; }, [dirty]);
  React.useEffect(() => {
    function beforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current) { event.preventDefault(); event.returnValue = ""; }
    }
    function beforeLeave(event: Event) {
      if (dirtyRef.current && !window.confirm("Discard unsaved edits and leave? Only the last server-saved draft will be recovered.")) event.preventDefault();
    }
    function linkClick(event: MouseEvent) {
      const link = (event.target as Element).closest?.("a[href]");
      if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || link.getAttribute("target") === "_blank") return;
      if (dirtyRef.current && !window.confirm("Discard unsaved edits and leave? Only the last server-saved draft will be recovered.")) { event.preventDefault(); event.stopPropagation(); }
    }
    // Navigation API cancellation happens before App Router traverses browser history.
    // Cross-document exits remain protected by beforeunload.
    const navigation = (window as Window & { navigation?: EventTarget }).navigation;
    function navigate(event: Event) {
      if ((event as Event & { navigationType?: string }).navigationType === "traverse" && event.cancelable) beforeLeave(event);
    }
    navigation?.addEventListener("navigate", navigate);
    window.addEventListener("beforeunload", beforeUnload);
    window.addEventListener("workflow:before-leave", beforeLeave);
    document.addEventListener("click", linkClick, true);
    return () => {
      navigation?.removeEventListener("navigate", navigate);
      window.removeEventListener("beforeunload", beforeUnload);
      window.removeEventListener("workflow:before-leave", beforeLeave);
      document.removeEventListener("click", linkClick, true);
    };
  }, []);
  return () => { dirtyRef.current = false; };
}

export function Field({ label, value, onChange, multiline = false, required = true, readOnly = false }: {
  label: string; value: string; onChange(value: string): void; multiline?: boolean; required?: boolean; readOnly?: boolean;
}) {
  const id = React.useId();
  return <label className="filter-field" htmlFor={id}>{label}{multiline
    ? <textarea id={id} value={value} required={required} readOnly={readOnly} onChange={event => onChange(event.target.value)} />
    : <input id={id} value={value} required={required} readOnly={readOnly} onChange={event => onChange(event.target.value)} />}</label>;
}
