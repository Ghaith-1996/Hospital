"use client";
import React from "react";
import { useRouter } from "next/navigation";
import { useDevelopmentSession } from "../../features/session/development-session";
import { ChevronIcon } from "../ui/icons";

export function UserSwitcher() {
  const { user, identities, pending, error, switchIdentity } = useDevelopmentSession();
  const [open, setOpen] = React.useState(false);
  const root = React.useRef<HTMLDivElement>(null);
  const trigger = React.useRef<HTMLButtonElement>(null);
  const initialFocus = React.useRef(0);
  React.useEffect(() => {
    if (!open) return;
    const items = root.current?.querySelectorAll<HTMLButtonElement>('[role="menuitem"]');
    items?.[initialFocus.current < 0 ? items.length - 1 : initialFocus.current]?.focus();
    function outside(event: PointerEvent) { if (!root.current?.contains(event.target as Node)) setOpen(false); }
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [open]);
  const router = useRouter();
  return <div className="user-switcher" ref={root} onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }}>
    <p className="simulation-pill">DEVELOPMENT AUTHENTICATION</p>
    {error && <p role="alert">{error}</p>}
    <button ref={trigger} className="user-switcher__trigger" type="button" disabled={pending} aria-haspopup="menu" aria-expanded={open} onClick={() => { initialFocus.current = 0; setOpen(!open); }} onKeyDown={event => { if (event.key === "ArrowDown" || event.key === "ArrowUp") { event.preventDefault(); initialFocus.current = event.key === "ArrowUp" ? -1 : 0; setOpen(true); } }}>
      <span className="user-switcher__avatar" aria-hidden="true">{user?.displayName.split(" ").map(part => part[0]).slice(0, 2).join("") ?? "?"}</span>
      <span className="user-switcher__identity"><span>{user?.displayName ?? "Select simulation identity"}</span><span>{user?.roles.join(", ") ?? "Backend session required"}</span></span><ChevronIcon />
    </button>
    {open && <div className="user-switcher__menu" role="menu" aria-label="Simulation users" onKeyDown={event => {
      if (event.key === "Escape") { event.preventDefault(); setOpen(false); trigger.current?.focus(); return; }
      const items = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')];
      const index = items.indexOf(document.activeElement as HTMLButtonElement);
      const target = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : event.key === "ArrowDown" ? (index + 1) % items.length : event.key === "ArrowUp" ? (index - 1 + items.length) % items.length : null;
      if (target !== null) { event.preventDefault(); items[target]?.focus(); }
      if (event.key === "Tab") { setOpen(false); trigger.current?.focus(); }
    }}>
      {identities.map(identity => <button key={identity.simulationHandle} className="user-switcher__item" role="menuitem" tabIndex={-1} type="button" disabled={pending} onClick={async () => {
        const guard = new Event("workflow:before-leave", { cancelable: true });
        if (!window.dispatchEvent(guard)) return;
        setOpen(false);
        const principal = await switchIdentity(identity.simulationHandle);
        if (principal) router.replace(principal.roles.some(role => role === "Practitioner" || role === "Physician") ? "/my-alerts" : "/alerts/new");
      }}><span><span>{identity.displayName}</span><span>{identity.roles.join(", ")}</span></span></button>)}
    </div>}
  </div>;
}
