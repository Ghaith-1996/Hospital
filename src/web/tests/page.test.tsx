import React from "react";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import HomePage from "../app/page";

describe("Phase 3 web shell", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => [],
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("rendersSimulationModeBanner", () => {
    render(<HomePage />);

    expect(screen.getByRole("status", { name: "SIMULATION MODE" })).toBeVisible();
  });

  it("rendersDevelopmentAuthenticationBanner", () => {
    render(<HomePage />);

    expect(screen.getByRole("status", { name: "DEVELOPMENT AUTHENTICATION" })).toBeVisible();
  });

  it("doesNotRenderDispatchControl", () => {
    render(<HomePage />);

    expect(screen.queryByRole("button", { name: /dispatch/i })).not.toBeInTheDocument();
  });

  it("hasAVisiblePageTitle", () => {
    render(<HomePage />);

    expect(screen.getByRole("heading", { name: "Critical Alerts Platform" })).toBeVisible();
  });

  it("showsAUserSwitcher", () => {
    render(<HomePage />);

    expect(screen.getByLabelText("Simulation user")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Development identity switcher" })).toBeVisible();
  });
});
