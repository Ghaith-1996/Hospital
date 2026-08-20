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

  it("doesNotShowUserSwitcherWhenDevelopmentIdentitiesAreUnavailable", async () => {
    render(<HomePage />);

    expect(await screen.findByText("Seeded identities are unavailable until the local API is running.")).toBeVisible();
    expect(screen.queryByLabelText("Simulation user")).not.toBeInTheDocument();
  });

  it("showsAUserSwitcherWhenSeededIdentitiesLoad", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        const url = String(input);
        if (url.includes("/api/dev/identities")) {
          return {
            ok: true,
            json: async () => [
              {
                displayName: "Jordan Lee",
                simulationHandle: "sim-operator-jordan",
                roles: ["Operator"],
              },
            ],
          };
        }

        return { ok: false, status: 401, json: async () => ({}) };
      }),
    );

    render(<HomePage />);

    expect(await screen.findByLabelText("Simulation user")).toBeVisible();
    expect(screen.getByRole("heading", { name: "Development identity switcher" })).toBeVisible();
  });
});
