"use client";
import React from "react";
import { isAlertApiError } from "../../lib/alerts";
import * as api from "../../lib/development-auth";

type Session = {
  user: api.CurrentUser | null;
  identities: api.DevelopmentIdentity[];
  pending: boolean;
  error: string | null;
  generation: number;
  switchIdentity(handle: string): Promise<api.CurrentUser | null>;
};
const Context = React.createContext<Session | null>(null);
export function DevelopmentSessionProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = React.useState<api.CurrentUser | null>(null);
  const [identities, setIdentities] = React.useState<api.DevelopmentIdentity[]>([]);
  const [pending, setPending] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [generation, setGeneration] = React.useState(0);
  const lock = React.useRef(false);
  React.useEffect(() => {
    let active = true;
    Promise.allSettled([api.getDevelopmentIdentities(), api.getCurrentUser()]).then(([list, current]) => {
      if (!active) return;
      if (list.status === "fulfilled") setIdentities(list.value);
      else setError("Development authentication unavailable. Check the simulation API and retry.");
      if (current.status === "fulfilled") setUser(current.value);
      else if (!isAlertApiError(current.reason) || current.reason.status !== 401) setError("Session unavailable. Check the simulation API and retry.");
      setPending(false);
    });
    return () => { active = false; };
  }, []);
  async function switchIdentity(handle: string) {
    if (lock.current || !identities.some(identity => identity.simulationHandle === handle)) return null;
    lock.current = true;
    setPending(true);
    setUser(null);
    setGeneration(value => value + 1);
    setError(null);
    try {
      await api.createDevelopmentSession(handle);
      const principal = await api.getCurrentUser();
      setUser(principal);
      return principal;
    } catch {
      setError("Session switch unavailable. Retry selecting a simulation identity.");
      return null;
    } finally { lock.current = false; setPending(false); }
  }
  return <Context.Provider value={{ user, identities, pending, error, generation, switchIdentity }}>{children}</Context.Provider>;
}
export function useDevelopmentSession() {
  const session = React.useContext(Context);
  if (!session) throw new Error("DevelopmentSessionProvider is required.");
  return session;
}
