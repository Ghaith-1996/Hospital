import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { UserSwitcher } from "../components/layout/user-switcher";
import { DevelopmentSessionProvider } from "../features/session/development-session";

const replace = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }), usePathname: () => "/alerts/new" }));
afterEach(() => vi.unstubAllGlobals());

test("selects a server-listed handle then shows the authenticated server principal", async () => {
  let signedIn = false;
  const calls: Array<{ path: string; body?: string }> = [];
  vi.stubGlobal("fetch", vi.fn(async (path: string, init?: RequestInit) => {
    calls.push({ path, body: init?.body as string });
    if (path.endsWith("/identities")) return Response.json([{ displayName: "Fictional Operator", simulationHandle: "sim-operator", roles: ["Operator"], organizationId: "sim-org" }]);
    if (path.endsWith("/session")) { signedIn = true; return new Response(null, { status: 204 }); }
    return signedIn ? Response.json({ userId: "server-user", displayName: "Server Operator", simulationHandle: "sim-operator", roles: ["Operator"], organizationId: "sim-org", developmentAuthentication: true }) : Response.json({}, { status: 401 });
  }));
  render(<DevelopmentSessionProvider><UserSwitcher /></DevelopmentSessionProvider>);
  fireEvent.click(await screen.findByRole("button", { name: /Select simulation identity/ }));
  const item = await screen.findByRole("menuitem", { name: /Fictional Operator/ });
  expect(item).toHaveFocus();
  fireEvent.keyDown(item, { key: "Escape" });
  expect(screen.getByRole("button", { name: /Select simulation identity/ })).toHaveFocus();
  expect(screen.queryByRole("menu")).not.toBeInTheDocument();
  fireEvent.keyDown(screen.getByRole("button", { name: /Select simulation identity/ }), { key: "ArrowDown" });
  fireEvent.click(await screen.findByRole("menuitem", { name: /Fictional Operator/ }));
  expect(await screen.findByText("Server Operator")).toBeVisible();
  expect(calls.find(call => call.path.endsWith("/session"))?.body).toBe(JSON.stringify({ simulationHandle: "sim-operator" }));
  expect(screen.getByText("DEVELOPMENT AUTHENTICATION")).toBeVisible();
  await waitFor(() => expect(replace).toHaveBeenCalledWith("/alerts/new"));
});

test("API failure cannot create a browser identity", async () => {
  vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("network")));
  render(<DevelopmentSessionProvider><UserSwitcher /></DevelopmentSessionProvider>);
  expect(await screen.findByRole("alert")).toHaveTextContent(/unavailable|retry/i);
  expect(screen.queryByText("Server Operator")).not.toBeInTheDocument();
});
