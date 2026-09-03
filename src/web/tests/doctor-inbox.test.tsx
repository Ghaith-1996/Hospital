import React from "react";
import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import DoctorInboxPage from "../app/my-alerts/page";
import { createSeedState } from "../features/alerts/seed";
import type { PrototypeState } from "../features/alerts/types";
import { renderPrototype } from "./test-utils";

vi.mock("next/link", () => ({
  default: ({
    href,
    children,
    className,
    "aria-label": ariaLabel,
  }: {
    href: string;
    children: React.ReactNode;
    className?: string;
    "aria-label"?: string;
  }) => React.createElement("a", { href, className, "aria-label": ariaLabel }, children),
}));

function marcState(): PrototypeState {
  return {
    ...createSeedState(),
    selectedUserId: "user-marc",
  };
}

describe("doctor inbox", () => {
  beforeEach(() => {
    window.history.pushState({}, "", "/my-alerts");
  });

  it("renders Dr. Marc's assigned inbox with exact tabs, table columns, and unread filtering", () => {
    renderPrototype(<DoctorInboxPage />, { state: marcState() });

    expect(screen.getByRole("heading", { name: "Inbox" })).toBeVisible();
    expect(screen.getByText("Alerts assigned to me.")).toBeVisible();
    expect(screen.getByRole("table", { name: "Fictional doctor inbox" })).toBeVisible();
    expect(screen.getAllByRole("row")).toHaveLength(4);
    expect(screen.getByRole("tab", { name: "All 3" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Unread 1" })).toBeVisible();

    const table = screen.getByRole("table", { name: "Fictional doctor inbox" });
    expect(within(table).getAllByRole("columnheader").map((header) => header.textContent)).toEqual([
      "Alert",
      "Patient Reference",
      "Urgency",
      "Status",
      "Received",
    ]);

    fireEvent.click(screen.getByRole("tab", { name: "Unread 1" }));

    expect(screen.getAllByText("Chest pain, hypotension").length).toBeGreaterThan(0);
    expect(screen.queryByText("Suspected sepsis")).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: /Open Chest pain, hypotension/ })[0]).toHaveAttribute(
      "href",
      "/my-alerts/alert-critical-1",
    );
  });

  it("filters completed and in-progress alerts by Marc's recipient state without showing unread sent alerts", () => {
    renderPrototype(<DoctorInboxPage />, { state: marcState() });

    expect(screen.getByRole("tab", { name: "In Progress 1" })).toBeVisible();
    expect(screen.getByRole("tab", { name: "Completed 1" })).toBeVisible();

    fireEvent.click(screen.getByRole("tab", { name: "In Progress 1" }));
    expect(screen.getAllByText("Respiratory distress").length).toBeGreaterThan(0);
    expect(screen.queryByText("Chest pain, hypotension")).not.toBeInTheDocument();
    expect(screen.queryByText("Suspected sepsis")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: "Completed 1" }));
    expect(screen.getAllByText("Suspected sepsis").length).toBeGreaterThan(0);
    expect(screen.queryByText("Respiratory distress")).not.toBeInTheDocument();
    expect(screen.getAllByRole("link", { name: /Open Suspected sepsis/ })[0]).toHaveAttribute(
      "href",
      "/my-alerts/alert-escalating-1",
    );
  });

  it("shows a useful empty state when the selected doctor has no assigned alerts", () => {
    const emptyMarcState = {
      ...marcState(),
      alerts: marcState().alerts.filter((alert) =>
        alert.recipients.every((recipient) => recipient.clinicianId !== "clinician-marc"),
      ),
    };

    renderPrototype(<DoctorInboxPage />, { state: emptyMarcState });

    expect(screen.getByRole("status", { name: "No alerts assigned to me." })).toBeVisible();
    expect(screen.getByText("This fictional inbox will list alerts assigned to the selected doctor.")).toBeVisible();
    expect(screen.queryByRole("table", { name: "Fictional doctor inbox" })).not.toBeInTheDocument();
  });

  it("does not show operator data when a non-doctor is selected and offers a Marc switch action", () => {
    renderPrototype(<DoctorInboxPage />);

    expect(screen.getByRole("status", { name: "Doctor inbox requires a fictional doctor." })).toBeVisible();
    expect(screen.getByText("Switch users to view alerts assigned to Dr. Marc Tremblay.")).toBeVisible();
    expect(screen.queryByRole("table", { name: "Fictional doctor inbox" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Switch to Dr. Marc" }));

    expect(screen.getByRole("heading", { name: "Inbox" })).toBeVisible();
    expect(screen.getByRole("table", { name: "Fictional doctor inbox" })).toBeVisible();
  });

  it("renders mobile cards with the same fields and explicit focusable links as the desktop table", () => {
    renderPrototype(<DoctorInboxPage />, { state: marcState() });

    const chestRow = within(screen.getByRole("table", { name: "Fictional doctor inbox" }))
      .getAllByRole("row")
      .find((row) => within(row).queryByText("Chest pain, hypotension"));
    expect(chestRow).toBeDefined();

    const card = screen.getByRole("article", { name: "Chest pain, hypotension alert card" });
    expect(within(card).getByText("Alert")).toBeVisible();
    expect(within(card).getAllByText("Chest pain, hypotension").length).toBeGreaterThan(0);
    expect(within(card).getByText("Patient Reference")).toBeVisible();
    expect(within(card).getByText("SIM-PAT-01578")).toBeVisible();
    expect(within(card).getByText("Urgency")).toBeVisible();
    expect(within(card).getByText("Critical")).toBeVisible();
    expect(within(card).getByText("Status")).toBeVisible();
    expect(within(card).getByText("New")).toBeVisible();
    expect(within(card).getByText("Received")).toBeVisible();

    const cardLink = within(card).getByRole("link", { name: "Open Chest pain, hypotension" });
    cardLink.focus();
    expect(cardLink).toHaveFocus();
    expect(cardLink).toHaveClass("focus-link");
    expect(cardLink).toHaveAttribute("href", "/my-alerts/alert-critical-1");
  });
});
