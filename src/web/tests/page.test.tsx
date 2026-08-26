import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import HomePage from "../app/page";
import DirectoryPage from "../app/directory/page";
import DirectoryImportPage from "../app/directory/import/page";

vi.mock("next/link", () => ({
  default: ({
    href,
    children,
    className,
  }: {
    href: string;
    children: React.ReactNode;
    className?: string;
  }) => React.createElement("a", { href, className }, children),
}));

describe("Phase 4 web shell", () => {
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

  it("directorySearchDoesNotDispatchAndRequiresAuthentication", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        const url = String(input);
        if (url.includes("/api/directory/practitioners")) {
          return { ok: false, status: 401, json: async () => ({}) };
        }

        return { ok: false, status: 404, json: async () => [] };
      }),
    );

    render(<DirectoryPage />);

    expect(screen.getByRole("heading", { name: "Fictional practitioner directory" })).toBeVisible();
    expect(screen.getByLabelText("Search practitioners")).toBeVisible();
    expect(screen.queryByRole("button", { name: /dispatch/i })).not.toBeInTheDocument();
    expect(await screen.findByText("Sign in with a seeded Operator or Administrator identity to search.")).toBeVisible();
  });

  it("rendersDirectoryFreshnessAndOnCallSynchronization", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        if (String(input).includes("/api/directory/practitioners")) {
          return {
            ok: true,
            status: 200,
            json: async () => [
              {
                practitionerId: "00000000-0000-0000-0000-000000000001",
                displayName: "Maya Chen",
                specialty: "Emergency Medicine",
                department: "Emergency",
                site: "North",
                roleTitle: "Emergency physician",
                simulationCode: "SIM-PRAC-0101",
                isActive: false,
                isStale: true,
                selectable: false,
                sourceSystem: "SIM-CSV",
                lastSynchronizedAtUtc: "2026-08-01T12:00:00Z",
                onCallTier: "Primary",
                onCallSourceSystem: "SIM-CSV",
                onCallLastSynchronizedAtUtc: "2026-08-01T12:30:00Z",
              },
            ],
          };
        }

        return { ok: false, status: 404, json: async () => ({}) };
      }),
    );

    render(<DirectoryPage />);

    expect(await screen.findByText("Maya Chen")).toBeVisible();
    expect(screen.getByText("Inactive / Stale")).toBeVisible();
    expect(screen.getByText("Primary (SIM-CSV) @ 2026-08-01T12:30:00.000Z")).toBeVisible();
  });

  it("invalidatesPreviewWhenTheSelectedCsvChanges", async () => {
    const preview = {
      sourceSystem: "SIM-CSV",
      parsedPractitionerCount: 1,
      insertCount: 1,
      updateCount: 0,
      rejectedCount: 0,
      errors: [],
      warnings: [],
      changes: [],
    };
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo) => {
        if (String(input).endsWith("/api/directory/imports/preview")) {
          return { ok: true, status: 200, json: async () => preview };
        }

        return { ok: false, status: 404, json: async () => ({}) };
      }),
    );

    render(<DirectoryImportPage />);
    const input = screen.getByLabelText("Simulation CSV");
    const previewButton = screen.getByRole("button", { name: "Preview import" });
    const applyButton = screen.getByRole("button", { name: "Apply import" });

    expect(applyButton).toBeDisabled();
    fireEvent.change(input, { target: { files: [new File(["first"], "first.csv", { type: "text/csv" })] } });
    fireEvent.click(previewButton);

    expect(await screen.findByText("Preview ready for SIM-CSV. Nothing was written.")).toBeVisible();
    expect(applyButton).toBeEnabled();

    fireEvent.change(input, { target: { files: [new File(["second"], "second.csv", { type: "text/csv" })] } });

    expect(screen.getByText("Choose a new preview before applying the selected CSV.")).toBeVisible();
    expect(applyButton).toBeDisabled();
  });
});
