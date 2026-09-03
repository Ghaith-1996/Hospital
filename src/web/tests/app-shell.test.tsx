import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AlertComposePage from "../app/alerts/[id]/compose/page";
import AlertRecipientsPage from "../app/alerts/[id]/recipients/page";
import NewAlertPage from "../app/alerts/new/page";
import DirectoryPage from "../app/directory/page";
import DirectoryImportPage from "../app/directory/import/page";
import HomePage from "../app/page";
import { AppShell } from "../components/layout/app-shell";
import { Tabs } from "../components/ui/tabs";
import { createSeedState } from "../features/alerts/seed";
import { PrototypeProvider } from "../features/alerts/prototype-store";
import type { PrototypeState } from "../features/alerts/types";

const navigation = vi.hoisted(() => ({
  mockReplace: vi.fn(),
  mockPush: vi.fn(),
  mockRedirect: vi.fn(),
  pathname: "/alerts/new",
}));

vi.mock("next/link", () => ({
  default: ({
    href,
    children,
    className,
    "aria-current": ariaCurrent,
    onClick,
  }: {
    href: string;
    children: React.ReactNode;
    className?: string;
    "aria-current"?: "page";
    onClick?: React.MouseEventHandler<HTMLAnchorElement>;
  }) => React.createElement("a", { href, className, "aria-current": ariaCurrent, onClick }, children),
}));

vi.mock("next/navigation", () => ({
  redirect: navigation.mockRedirect,
  usePathname: () => navigation.pathname,
  useRouter: () => ({
    push: navigation.mockPush,
    replace: navigation.mockReplace,
  }),
}));

function renderShell(state: PrototypeState = createSeedState()) {
  return render(
    <PrototypeProvider initialState={state}>
      <AppShell>
        <h1>Route content</h1>
      </AppShell>
    </PrototypeProvider>,
  );
}

function doctorState() {
  return {
    ...createSeedState(),
    selectedUserId: "user-marc",
  };
}

describe("prototype app shell", () => {
  beforeEach(() => {
    navigation.mockReplace.mockClear();
    navigation.mockPush.mockClear();
    navigation.mockRedirect.mockClear();
    navigation.pathname = "/alerts/new";
  });

  it("renders operator navigation and changes to doctor navigation after user switch", async () => {
    renderShell();

    expect(screen.getByRole("status", { name: "SIMULATION" })).toBeVisible();
    expect(screen.getByRole("navigation", { name: "Operator navigation" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Alert Doctor" })).toHaveAttribute("href", "/alerts/new");
    expect(screen.getByRole("link", { name: "Alerts" })).toHaveAttribute("href", "/alerts");
    expect(screen.getByRole("button", { name: "Directory — Coming later" })).toBeDisabled();

    fireEvent.click(screen.getByRole("button", { name: /Sophie Bernard/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: /Dr. Marc Tremblay/ }));

    expect(navigation.mockReplace).toHaveBeenCalledWith("/my-alerts");
    expect(screen.getByRole("navigation", { name: "Doctor navigation" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Inbox" })).toHaveAttribute("href", "/my-alerts");
  });

  it("resets demo data from the user menu without leaving the current role stale", async () => {
    const state = doctorState();
    renderShell(state);

    fireEvent.click(screen.getByRole("button", { name: /Dr. Marc Tremblay/ }));
    fireEvent.click(screen.getByRole("menuitem", { name: "Reset demo data" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: /Sophie Bernard/ })).toBeVisible();
    });
    expect(navigation.mockReplace).toHaveBeenCalledWith("/alerts/new");
    expect(screen.getByRole("navigation", { name: "Operator navigation" })).toBeVisible();
  });

  it("marks the active link and toggles the tablet drawer semantics", () => {
    navigation.pathname = "/alerts";
    renderShell();

    expect(screen.getByRole("link", { name: "Alerts" })).toHaveAttribute("aria-current", "page");
    const menuButton = screen.getByRole("button", { name: "Open navigation" });
    expect(menuButton).toHaveAttribute("aria-expanded", "false");

    fireEvent.click(menuButton);

    expect(menuButton).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Close navigation" })).toBeVisible();
  });

  it("marks only the exact alert creation route as current", () => {
    navigation.pathname = "/alerts/new";
    renderShell();

    expect(screen.getByRole("link", { name: "Alert Doctor" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Alerts" })).not.toHaveAttribute("aria-current");
  });

  it("withholds role navigation until prototype hydration completes", async () => {
    const storedState = JSON.stringify(doctorState());
    const storage = {
      getItem: vi.fn(() => storedState),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    };

    render(
      <PrototypeProvider storage={storage}>
        <AppShell>
          <h1>Hydrating route</h1>
        </AppShell>
      </PrototypeProvider>,
    );

    expect(screen.queryByRole("navigation", { name: /navigation/i })).not.toBeInTheDocument();

    await screen.findByRole("navigation", { name: "Doctor navigation" });
    expect(screen.queryByRole("navigation", { name: "Operator navigation" })).not.toBeInTheDocument();
  });

  it("shows root loading before replacing to the hydrated role route", async () => {
    const storage = {
      getItem: vi.fn(() => JSON.stringify(doctorState())),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    };

    render(
      <PrototypeProvider storage={storage}>
        <HomePage />
      </PrototypeProvider>,
    );

    expect(screen.getByRole("status", { name: "Loading fictional demo workspace" })).toBeVisible();

    await waitFor(() => {
      expect(navigation.mockReplace).toHaveBeenCalledWith("/my-alerts");
    });
  });

  it("routes a complete local alert draft to review instead of legacy compose", () => {
    render(
      <PrototypeProvider initialState={createSeedState()}>
        <NewAlertPage />
      </PrototypeProvider>,
    );

    fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "SIM-PAT-2222" } });
    fireEvent.change(screen.getByLabelText("Case Details"), {
      target: { value: "SIMULATION: fictional shell route alert." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add Dr. Marc Tremblay" }));
    fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

    expect(navigation.mockPush).toHaveBeenCalledWith(expect.stringMatching(/^\/alerts\/alert-[a-z0-9-]+\/review$/));
  });

  it("renders approved local-only directory coming-later states without legacy controls", () => {
    const { rerender } = render(<DirectoryPage />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: "Directory is coming later" })).toBeVisible();
    expect(
      screen.getByText(
        "The redesigned frontend is local-only. A future backend phase will reconnect fictional directory management.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "Alert Doctor" })).toHaveAttribute("href", "/alerts/new");
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /import/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/search/i)).not.toBeInTheDocument();

    rerender(<DirectoryImportPage />);

    expect(screen.getAllByRole("heading", { level: 1 })).toHaveLength(1);
    expect(screen.getByRole("heading", { level: 1, name: "Directory is coming later" })).toBeVisible();
    expect(
      screen.getByText(
        "The redesigned frontend is local-only. A future backend phase will reconnect fictional directory management.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("link", { name: "Alert Doctor" })).toHaveAttribute("href", "/alerts/new");
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /import/i })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/search/i)).not.toBeInTheDocument();
  });

  it("redirects legacy alert compose and recipients routes to alert creation", () => {
    AlertComposePage();
    AlertRecipientsPage();

    expect(navigation.mockRedirect).toHaveBeenCalledTimes(2);
    expect(navigation.mockRedirect).toHaveBeenNthCalledWith(1, "/alerts/new");
    expect(navigation.mockRedirect).toHaveBeenNthCalledWith(2, "/alerts/new");
  });

  it("moves the roving tab stop with arrow-key focus", () => {
    render(
      <Tabs
        ariaLabel="Demo tabs"
        value="all"
        onChange={vi.fn()}
        tabs={[
          { value: "all", label: "All" },
          { value: "draft", label: "Draft", count: 2 },
          { value: "sent", label: "Sent" },
        ]}
      />,
    );

    const allTab = screen.getByRole("tab", { name: "All" });
    const draftTab = screen.getByRole("tab", { name: "Draft 2" });

    allTab.focus();
    fireEvent.keyDown(allTab, { key: "ArrowRight" });

    expect(draftTab).toHaveFocus();
    expect(draftTab).toHaveAttribute("tabindex", "0");
    expect(allTab).toHaveAttribute("tabindex", "-1");
    expect(allTab).toHaveAttribute("aria-selected", "true");
  });
});
