import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { Providers } from "../app/providers";
vi.mock("next/navigation", () => ({ usePathname: () => "/alerts/new", useRouter: () => ({ replace: vi.fn() }) }));
afterEach(() => vi.unstubAllGlobals());
test("same-route identity switching clears and remounts protected form state", async () => {
  let handle = "sim-first";
  const identities = ["sim-first", "sim-second"].map(simulationHandle => ({ simulationHandle, displayName: simulationHandle, roles: ["Operator"], organizationId: "sim-org" }));
  vi.stubGlobal("fetch", vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/identities")) return Response.json(identities);
    if (path.endsWith("/session")) { handle = JSON.parse(init!.body as string).simulationHandle; return new Response(null, { status: 204 }); }
    return Response.json({ ...identities.find(identity => identity.simulationHandle === handle), userId: handle, developmentAuthentication: true });
  }));
  function Form() { const [text, setText] = React.useState(""); return <label>Protected form<input value={text} onChange={event => setText(event.target.value)} /></label>; }
  render(<Providers><Form /></Providers>);
  fireEvent.change(await screen.findByLabelText("Protected form"), { target: { value: "SIMULATION: prior identity edits" } });
  fireEvent.click(screen.getByRole("button", { name: /sim-first Operator/ }));
  fireEvent.click(screen.getByRole("menuitem", { name: /sim-second Operator/ }));
  expect(await screen.findByLabelText("Protected form")).toHaveValue("");
  expect(screen.getAllByText("SIMULATION MODE").length).toBeGreaterThan(0);
});
