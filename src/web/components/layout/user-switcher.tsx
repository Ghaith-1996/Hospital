"use client";

import React from "react";
import { useRouter } from "next/navigation";
import { usePrototype } from "../../features/alerts/prototype-store";
import type { PrototypeUser } from "../../features/alerts/types";
import { ChevronIcon, UserIcon } from "../ui/icons";

function routeForUser(user: PrototypeUser) {
  return user.role === "doctor" ? "/my-alerts" : "/alerts/new";
}

export function UserSwitcher() {
  const { state, selectUser, resetDemo } = usePrototype();
  const router = useRouter();
  const [open, setOpen] = React.useState(false);
  const menuRef = React.useRef<HTMLDivElement | null>(null);
  const currentUser = state.users.find((user) => user.id === state.selectedUserId) ?? state.users[0];

  React.useEffect(() => {
    if (!open) return;

    function closeOnOutsidePointer(event: PointerEvent) {
      if (menuRef.current?.contains(event.target as Node)) return;
      setOpen(false);
    }

    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setOpen(false);
      }
    }

    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [open]);

  function chooseUser(user: PrototypeUser) {
    selectUser(user.id);
    setOpen(false);
    router.replace(routeForUser(user));
  }

  function reset() {
    resetDemo();
    setOpen(false);
    router.replace("/alerts/new");
  }

  return (
    <div className="user-switcher" ref={menuRef}>
      <button
        className="user-switcher__trigger"
        type="button"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((current) => !current)}
      >
        <span className="user-switcher__avatar" aria-hidden="true">
          {currentUser.initials}
        </span>
        <span className="user-switcher__identity">
          <span>{currentUser.displayName}</span>
          <span>{currentUser.title}</span>
        </span>
        <ChevronIcon className="user-switcher__chevron" />
      </button>

      {open ? (
        <div className="user-switcher__menu" role="menu" aria-label="Simulation users">
          {state.users.map((user) => (
            <button
              className="user-switcher__item"
              key={user.id}
              type="button"
              role="menuitem"
              onClick={() => chooseUser(user)}
            >
              <span className="user-switcher__avatar" aria-hidden="true">
                {user.initials}
              </span>
              <span>
                <span>{user.displayName}</span>
                <span>{user.title}</span>
              </span>
              {user.id === currentUser.id ? <UserIcon className="user-switcher__current" /> : null}
            </button>
          ))}
          <div className="user-switcher__separator" role="separator" />
          <button className="user-switcher__item" type="button" role="menuitem" onClick={reset}>
            Reset demo data
          </button>
        </div>
      ) : null}
    </div>
  );
}
