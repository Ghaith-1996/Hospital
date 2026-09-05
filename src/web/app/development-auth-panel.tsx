"use client";

import React, { useEffect, useState } from "react";

type SeededIdentity = {
  displayName: string;
  simulationHandle: string;
  roles: string[];
};

type CurrentUser = {
  displayName: string;
  simulationHandle: string;
  roles: string[];
};

export function DevelopmentAuthPanel() {
  const [identities, setIdentities] = useState<SeededIdentity[]>([]);
  const [currentUser, setCurrentUser] = useState<CurrentUser | null>(null);
  const [status, setStatus] = useState("Loading seeded identities.");

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const identitiesResponse = await fetch("/api/v1/dev/identities");
        if (!identitiesResponse.ok) {
          if (!cancelled) {
            setStatus("Seeded identities are unavailable until the local API is running.");
          }
          return;
        }

        const loaded = (await identitiesResponse.json()) as SeededIdentity[];
        const meResponse = await fetch("/api/v1/me");
        const me = meResponse.ok ? ((await meResponse.json()) as CurrentUser) : null;
        if (!cancelled) {
          setIdentities(loaded);
          setCurrentUser(me);
          setStatus("Fictional seeded identities only. This is not hospital SSO.");
        }
      } catch {
        if (!cancelled) {
          setStatus("Seeded identities are unavailable until the local API is running.");
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  async function switchUser(simulationHandle: string) {
    const response = await fetch("/api/v1/dev/session", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ simulationHandle }),
    });
    if (!response.ok) {
      setStatus("The selected simulation identity could not be signed in.");
      return;
    }

    const meResponse = await fetch("/api/v1/me");
    if (meResponse.ok) {
      setCurrentUser((await meResponse.json()) as CurrentUser);
      setStatus("Fictional seeded identities only. This is not hospital SSO.");
    }
  }

  return (
    <section className="auth-panel" aria-labelledby="dev-auth-title">
      <h2 id="dev-auth-title">Development identity switcher</h2>
      <p>{status}</p>
      {currentUser ? (
        <p>
          Signed in as {currentUser.displayName} ({currentUser.roles.join(", ")})
        </p>
      ) : (
        <p>No simulation user is signed in.</p>
      )}
      {identities.length > 0 ? (
        <>
          <label htmlFor="simulation-identity">Simulation user</label>
          <select
            id="simulation-identity"
            value={currentUser?.simulationHandle ?? ""}
            onChange={(event) => {
              if (event.target.value) {
                void switchUser(event.target.value);
              }
            }}
          >
            <option value="">Select a seeded identity</option>
            {identities.map((identity) => (
              <option key={identity.simulationHandle} value={identity.simulationHandle}>
                {identity.displayName} ({identity.roles.join(", ")})
              </option>
            ))}
          </select>
        </>
      ) : null}
    </section>
  );
}
