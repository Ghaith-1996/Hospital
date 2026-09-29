import React from "react";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { AuditEvents } from "../features/connected/audit-events";
import { AppShell } from "../components/layout/app-shell";

const session = vi.hoisted(() => ({ user: { userId: "fictional-user", roles: ["Auditor"] }, pending: false, generation: 0 }));
vi.mock("next/navigation", () => ({ usePathname: () => "/admin/audit" }));
vi.mock("../features/session/development-session", () => ({ useDevelopmentSession: () => session }));
vi.mock("../components/layout/user-switcher", () => ({ UserSwitcher: () => null }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); session.user.roles = ["Auditor"]; });
const id = "11111111-1111-4111-8111-111111111111";
const correlation = "22222222-2222-4222-8222-222222222222";
const event = { id, action: "alert.confirmed", resourceType: "alert", resourceId: id,
  actorType: "user", actorUserId: id, outcome: "succeeded", correlationId: correlation,
  occurredAtUtc: "2026-09-19T12:00:00Z", metadata: { version: 2, channel: "Sms" } };
function respond(body: unknown, status = 200) { return Response.json(body, { status }); }

test.each([401, 403, 500])("audit errors show fixed recovery and retry without reflecting server errors (%s)", async status => {
  const sentinel = "SIM-SECRET-PHASE10-SENTINEL";
  const fetcher = vi.fn().mockResolvedValueOnce(respond({ detail: sentinel }, status))
    .mockResolvedValue(respond({ events: [], nextCursor: null }));
  vi.stubGlobal("fetch", fetcher);
  render(<AuditEvents />);
  expect(await screen.findByRole("alert")).toBeVisible();
  expect(document.body.textContent?.includes(sentinel)).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "Retry audit query" }));
  expect(await screen.findByText("No audit events match these filters.")).toBeVisible();
});

test.each([{ events: "wrong", nextCursor: null }, { events: [event], nextCursor: "invalid" }])(
  "invalid audit response has safe recovery", async response => {
    vi.stubGlobal("fetch", vi.fn(async () => respond(response)));
    render(<AuditEvents />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/Audit response was invalid/);
  });

test("metadata projection drops protected values even on known keys and top-level payload additions", async () => {
  const sentinel = "SIM-PATIENT-PHASE10-SENTINEL";
  vi.stubGlobal("fetch", vi.fn(async () => respond({ events: [{ ...event, patientReference: sentinel,
    metadata: { version: 2, channel: sentinel, message: sentinel, nested: { content: sentinel } } }], nextCursor: null })));
  render(<AuditEvents />);
  await screen.findByRole("table");
  expect(document.body.textContent?.includes(sentinel)).toBe(false);
  expect(screen.getByText("version: 2")).toBeVisible();
});

test("untrusted top-level strings do not render", async () => {
  const sentinel = "SIM-APPROVED-MESSAGE-DO-NOT-LOG";
  vi.stubGlobal("fetch", vi.fn(async () => respond({ events: [{ ...event, action: sentinel }], nextCursor: null })));
  render(<AuditEvents />);
  await screen.findByRole("alert");
  expect(document.body.textContent?.includes(sentinel)).toBe(false);
});

test("unsafe correlation is rejected before any query URL is created", async () => {
  const sentinel = "phase10@example.invalid";
  const fetcher = vi.fn(async () => respond({ events: [], nextCursor: null }));
  vi.stubGlobal("fetch", fetcher);
  render(<AuditEvents />);
  await screen.findByText("No audit events match these filters.");
  fireEvent.change(screen.getByLabelText("Correlation ID"), { target: { value: sentinel } });
  fireEvent.submit(screen.getByRole("form", { name: "Audit filters" }));
  await screen.findByRole("alert");
  expect(fetcher).toHaveBeenCalledTimes(1);
});

test.each(["SystemAdministrator"])(
  "audit navigation follows the server role (%s)", role => {
    session.user.roles = [role];
    render(<AppShell><p>Simulation content</p></AppShell>);
    expect(screen.queryByRole("link", { name: "Audit" }) !== null).toBe(["Auditor", "SystemAdministrator"].includes(role));
  });
