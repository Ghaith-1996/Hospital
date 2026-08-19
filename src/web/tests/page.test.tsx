import React from "react";
import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import HomePage from "../app/page";

describe("Phase 1 web shell", () => {
  it("rendersSimulationModeBanner", () => {
    render(<HomePage />);

    expect(screen.getByRole("status", { name: "SIMULATION MODE" })).toBeVisible();
  });

  it("doesNotRenderDispatchControl", () => {
    render(<HomePage />);

    expect(screen.queryByRole("button", { name: /dispatch/i })).not.toBeInTheDocument();
  });

  it("hasAVisiblePageTitle", () => {
    render(<HomePage />);

    expect(screen.getByRole("heading", { name: "Critical Alerts Platform" })).toBeVisible();
  });
});
