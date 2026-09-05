"use client";
import React from "react";
import { useRouter } from "next/navigation";
import { useDevelopmentSession } from "../../features/session/development-session";
import { ChevronIcon } from "../ui/icons";

export function UserSwitcher() {
  const { user, identities, pending, error, switchIdentity } = useDevelopmentSession();
  const [open, setOpen] = React.useState(false);
  const router = useRouter();
  return <div className="user-switcher">
    <p className="simulation-pill">DEVELOPMENT AUTHENTICATION</p>
    {error && <p role="alert">{error}</p>}
    <button className="user-switcher__trigger" type="button" disabled={pending} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}>
      <span className="user-switcher__avatar" aria-hidden="true">{user?.displayName.split(" ").map(part => part[0]).slice(0, 2).join("") ?? "?"}</span>
      <span className="user-switcher__identity"><span>{user?.displayName ?? "Select simulation identity"}</span><span>{user?.roles.join(", ") ?? "Backend session required"}</span></span><ChevronIcon />
    </button>
    {open && <div className="user-switcher__menu" role="menu" aria-label="Simulation users" onKeyDown={event => { if (event.key === "Escape") setOpen(false); }}>
      {identities.map(identity => <button key={identity.simulationHandle} className="user-switcher__item" role="menuitem" type="button" disabled={pending} onClick={async () => {
        const guard = new Event("workflow:before-leave", { cancelable: true });
        if (!window.dispatchEvent(guard)) return;
        setOpen(false);
        const principal = await switchIdentity(identity.simulationHandle);
        if (principal) router.replace(principal.roles.some(role => role === "Practitioner" || role === "Physician") ? "/my-alerts" : "/alerts/new");
      }}><span><span>{identity.displayName}</span><span>{identity.roles.join(", ")}</span></span></button>)}
    </div>}
  </div>;
}
