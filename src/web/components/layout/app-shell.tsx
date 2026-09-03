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

const drawerFocusableSelector = [
  "a[href]",
  "button:not([disabled])",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

function matchesPath(pathname: string, href: string) {
  return pathname === href || (href !== "/" && pathname.startsWith(`${href}/`));
}

function activeNavigationHref(pathname: string, navigation: NavigationItem[]) {
  return [...navigation]
    .sort((left, right) => right.href.length - left.href.length)
    .find((item) => matchesPath(pathname, item.href))?.href;
}

function getDrawerModeSnapshot() {
  return typeof window !== "undefined" && typeof window.matchMedia === "function"
    ? window.matchMedia("(max-width: 960px)").matches
    : false;
}

function subscribeDrawerMode(onStoreChange: () => void) {
  if (typeof window === "undefined" || typeof window.matchMedia !== "function") return () => undefined;

  const query = window.matchMedia("(max-width: 960px)");
  query.addEventListener("change", onStoreChange);
  return () => query.removeEventListener("change", onStoreChange);
}

function useDrawerMode() {
  return React.useSyncExternalStore(subscribeDrawerMode, getDrawerModeSnapshot, () => false);
}

export function AppShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { hydrated, storageError, state } = usePrototype();
  const [drawerOpen, setDrawerOpen] = React.useState(false);
  const drawerMode = useDrawerMode();
  const menuButtonRef = React.useRef<HTMLButtonElement | null>(null);
  const closeButtonRef = React.useRef<HTMLButtonElement | null>(null);
  const sidebarRef = React.useRef<HTMLElement | null>(null);
  const currentUser = state.users.find((user) => user.id === state.selectedUserId) ?? state.users[0];
  const navigation = currentUser.role === "doctor" ? doctorNavigation : operatorNavigation;
  const navigationLabel = currentUser.role === "doctor" ? "Doctor navigation" : "Operator navigation";
  const activeHref = activeNavigationHref(pathname, navigation);
  const roleNavigationReady = hydrated;
  const modalDrawerOpen = drawerMode && drawerOpen;

  React.useEffect(() => {
    document.body.classList.toggle("drawer-open", modalDrawerOpen);
    return () => {
      document.body.classList.remove("drawer-open");
    };
  }, [modalDrawerOpen]);

  React.useEffect(() => {
    if (!modalDrawerOpen) return;

    closeButtonRef.current?.focus();

    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === "Escape") {
        setDrawerOpen(false);
        menuButtonRef.current?.focus();
      }
    }

    function trapDrawerFocus(event: KeyboardEvent) {
      if (event.key !== "Tab") return;

      const focusable = Array.from(sidebarRef.current?.querySelectorAll<HTMLElement>(drawerFocusableSelector) ?? []);
      if (focusable.length === 0) return;

      const first = focusable[0];
      const last = focusable[focusable.length - 1];

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      }

      if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener("keydown", closeOnEscape);
    document.addEventListener("keydown", trapDrawerFocus);
    return () => {
      document.removeEventListener("keydown", closeOnEscape);
      document.removeEventListener("keydown", trapDrawerFocus);
    };
  }, [modalDrawerOpen]);

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

      {modalDrawerOpen ? <div className="drawer-scrim" aria-hidden="true" onClick={closeDrawer} /> : null}

      <aside
        className={`sidebar ${modalDrawerOpen ? "sidebar--open" : ""}`}
        id="prototype-sidebar"
        ref={sidebarRef}
        aria-label="Prototype sidebar"
        aria-hidden={drawerMode && !drawerOpen ? true : undefined}
        aria-modal={modalDrawerOpen ? true : undefined}
        inert={drawerMode && !drawerOpen ? true : undefined}
        role={modalDrawerOpen ? "dialog" : undefined}
      >
        <div className="sidebar__top">
          <div className="brand">
            <ShieldIcon className="brand__mark" />
            <div>
              <span>Critical Alerts</span>
              <span>Simulation demo</span>
            </div>
          </div>
          <button
            className="icon-button sidebar__close"
            ref={closeButtonRef}
            type="button"
            aria-label="Close navigation"
            onClick={closeDrawer}
          >
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

      <main className="app-shell__main" id="main-content" tabIndex={-1} inert={modalDrawerOpen ? true : undefined}>
        <div className="app-shell__content">
          {storageError ? <ScreenState kind="recoverable-storage" label={storageError} headingLevel="h2" /> : null}
          {children}
        </div>
      </main>
    </div>
  );
}
