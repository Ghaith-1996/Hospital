"use client";

import React from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { usePrototype } from "../../features/alerts/prototype-store";
import { BellIcon, CloseIcon, DirectoryIcon, InboxIcon, ListIcon, MenuIcon, ReportIcon, SettingsIcon, ShieldIcon } from "../ui/icons";
import { ScreenState } from "../ui/screen-state";
import { UserSwitcher } from "./user-switcher";

type NavigationItem = {
  label: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
};

const operatorNavigation: NavigationItem[] = [
  { label: "Alert Doctor", href: "/alerts/new", icon: BellIcon },
  { label: "Alerts", href: "/alerts", icon: ListIcon },
];

const doctorNavigation: NavigationItem[] = [{ label: "Inbox", href: "/my-alerts", icon: InboxIcon }];

const comingLaterItems = [
  { label: "Directory", icon: DirectoryIcon },
  { label: "Reports", icon: ReportIcon },
  { label: "Settings", icon: SettingsIcon },
];

function matchesPath(pathname: string, href: string) {
  return pathname === href || (href !== "/" && pathname.startsWith(`${href}/`));
}

function activeNavigationHref(pathname: string, navigation: NavigationItem[]) {
  return [...navigation]
    .sort((left, right) => right.href.length - left.href.length)
    .find((item) => matchesPath(pathname, item.href))?.href;
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { hydrated, storageError, state } = usePrototype();
  const [drawerOpen, setDrawerOpen] = React.useState(false);
  const initialHydratedRef = React.useRef(hydrated);
  const [roleNavigationReady, setRoleNavigationReady] = React.useState(hydrated);
  const menuButtonRef = React.useRef<HTMLButtonElement | null>(null);
  const currentUser = state.users.find((user) => user.id === state.selectedUserId) ?? state.users[0];
  const navigation = currentUser.role === "doctor" ? doctorNavigation : operatorNavigation;
  const navigationLabel = currentUser.role === "doctor" ? "Doctor navigation" : "Operator navigation";
  const activeHref = activeNavigationHref(pathname, navigation);

  React.useEffect(() => {
    if (!hydrated) {
      setRoleNavigationReady(false);
      return;
    }

    if (initialHydratedRef.current) {
      setRoleNavigationReady(true);
      return;
    }

    const handle = window.setTimeout(() => setRoleNavigationReady(true), 0);
    return () => window.clearTimeout(handle);
  }, [hydrated, currentUser.role]);

  React.useEffect(() => {
    document.body.classList.toggle("drawer-open", drawerOpen);
    return () => {
      document.body.classList.remove("drawer-open");
    };
  }, [drawerOpen]);

  React.useEffect(() => {
    if (!drawerOpen) return;

    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setDrawerOpen(false);
        menuButtonRef.current?.focus();
      }
    }

    document.addEventListener("keydown", closeOnEscape);
    return () => document.removeEventListener("keydown", closeOnEscape);
  }, [drawerOpen]);

  function closeDrawer() {
    setDrawerOpen(false);
    menuButtonRef.current?.focus();
  }

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content">
        Skip to content
      </a>
      <header className="mobile-topbar">
        <button
          className="icon-button"
          ref={menuButtonRef}
          type="button"
          aria-label="Open navigation"
          aria-controls="prototype-sidebar"
          aria-expanded={drawerOpen}
          onClick={() => setDrawerOpen(true)}
        >
          <MenuIcon />
        </button>
        <div className="brand brand--compact">
          <ShieldIcon className="brand__mark" />
          <span>Critical Alerts</span>
        </div>
        <span className="simulation-pill" aria-hidden="true">
          SIMULATION
        </span>
      </header>

      {drawerOpen ? <div className="drawer-scrim" aria-hidden="true" onClick={closeDrawer} /> : null}

      <aside className={`sidebar ${drawerOpen ? "sidebar--open" : ""}`} id="prototype-sidebar" aria-label="Prototype sidebar">
        <div className="sidebar__top">
          <div className="brand">
            <ShieldIcon className="brand__mark" />
            <div>
              <span>Critical Alerts</span>
              <span>Simulation demo</span>
            </div>
          </div>
          <button className="icon-button sidebar__close" type="button" aria-label="Close navigation" onClick={closeDrawer}>
            <CloseIcon />
          </button>
          <span className="simulation-pill simulation-pill--sidebar" role="status" aria-label="SIMULATION">
            SIMULATION
          </span>
        </div>

        {roleNavigationReady ? (
          <nav className="sidebar__nav" aria-label={navigationLabel}>
            {navigation.map((item) => {
              const Icon = item.icon;
              const active = activeHref === item.href;
              return (
                <Link
                  className={`nav-item ${active ? "nav-item--active" : ""}`}
                  key={item.href}
                  href={item.href}
                  aria-current={active ? "page" : undefined}
                  onClick={() => setDrawerOpen(false)}
                >
                  <Icon className="nav-item__icon" />
                  <span>{item.label}</span>
                </Link>
              );
            })}
            {comingLaterItems.map((item) => {
              const Icon = item.icon;
              return (
                <button
                  className="nav-item nav-item--disabled"
                  key={item.label}
                  type="button"
                  title="Coming later"
                  aria-label={`${item.label} — Coming later`}
                  disabled
                >
                  <Icon className="nav-item__icon" />
                  <span>{item.label}</span>
                </button>
              );
            })}
          </nav>
        ) : (
          <div className="sidebar__loading" aria-hidden="true" />
        )}

        <UserSwitcher />
      </aside>

      <main className="app-shell__main" id="main-content" tabIndex={-1}>
        <div className="app-shell__content">
          {storageError ? <ScreenState kind="recoverable-storage" label={storageError} /> : null}
          {children}
        </div>
      </main>
    </div>
  );
}
