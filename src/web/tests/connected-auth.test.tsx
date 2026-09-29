import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { UserSwitcher } from "../components/layout/user-switcher";
import { DevelopmentSessionProvider } from "../features/session/development-session";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }), usePathname: () => "/alerts/new" }));
afterEach(() => vi.unstubAllGlobals());

test("identity menu focuses its first item and restores trigger focus on Escape", async () => {
  vi.stubGlobal("fetch", vi.fn(async (path: string) => {
    if (path.endsWith("/identities")) return Response.json([{ displayName: "Fictional Operator", simulationHandle: "sim-operator", roles: ["Operator"], organizationId: "sim-org" }]);
    return Response.json({}, { status: 401 });
  }));
  render(<DevelopmentSessionProvider><UserSwitcher /></DevelopmentSessionProvider>);
  fireEvent.click(await screen.findByRole("button", { name: /Select simulation identity/ }));
  const item = await screen.findByRole("menuitem", { name: /Fictional Operator/ });
  expect(item).toHaveFocus();
  fireEvent.keyDown(item, { key: "Escape" });
  expect(screen.getByRole("button", { name: /Select simulation identity/ })).toHaveFocus();
  expect(screen.queryByRole("menu")).not.toBeInTheDocument();
});
