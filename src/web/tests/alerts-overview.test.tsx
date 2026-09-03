import React from "react";
import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AlertsOverviewPage from "../app/alerts/page";
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

describe("alerts overview", () => {
  beforeEach(() => {
    window.history.pushState({}, "", "/alerts");
  });

  it("shows tab-specific empty copy when the selected status tab has no alerts", () => {
    renderPrototype(<AlertsOverviewPage />);

    fireEvent.click(screen.getByRole("tab", { name: "Cancelled" }));

    expect(screen.getByText("No cancelled alerts yet.")).toBeVisible();
    expect(screen.getByText("Other fictional alerts exist, but none are in the currently selected tab.")).toBeVisible();
    expect(screen.queryByText("No alerts are available.")).not.toBeInTheDocument();
  });

  it("renders the seeded overview table, tab filtering, filter drawer actions, and mobile card fields", () => {
    renderPrototype(<AlertsOverviewPage />);

    expect(screen.getByRole("heading", { name: "Alerts" })).toBeVisible();
    expect(screen.getByRole("table", { name: "Fictional alerts" })).toBeVisible();
    expect(screen.getAllByRole("row")).toHaveLength(6);

    fireEvent.click(screen.getByRole("tab", { name: "Draft" }));
    expect(screen.getAllByText("SIM-PAT-1002").length).toBeGreaterThan(0);
    expect(screen.queryByText("SIM-PAT-01578")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: "All" }));
    fireEvent.click(screen.getByRole("button", { name: "Filters" }));
    fireEvent.change(screen.getByLabelText("Urgency"), { target: { value: "critical" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(screen.getByRole("button", { name: "Filters 1 active filter" })).toBeVisible();
    expect(screen.getAllByText("Critical").length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("button", { name: "Filters 1 active filter" }));
    fireEvent.click(screen.getByRole("button", { name: "Clear filters" }));

    expect(screen.getAllByRole("row")).toHaveLength(6);

    fireEvent.click(screen.getByRole("button", { name: "Filters" }));
    fireEvent.change(screen.getByLabelText("Department"), { target: { value: "Fictional Neurology" } });
    fireEvent.change(screen.getByLabelText("Status"), { target: { value: "resolved" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));

    expect(screen.getByText("No alerts match these filters.")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Clear filters" }));

    expect(screen.getAllByRole("link", { name: "Open SIM-PAT-01578" })[0]).toHaveAttribute(
      "href",
      "/alerts/alert-critical-1",
    );

    const cards = screen.getAllByRole("article");
    expect(cards).toHaveLength(5);

    const firstCard = cards[0];
    expect(within(firstCard).getByText("Patient Reference")).toBeVisible();
    expect(within(firstCard).getByText("Urgency")).toBeVisible();
    expect(within(firstCard).getByText("Status")).toBeVisible();
    expect(within(firstCard).getByText("Recipients")).toBeVisible();
    expect(within(firstCard).getByText("Last Updated")).toBeVisible();
  });
});
