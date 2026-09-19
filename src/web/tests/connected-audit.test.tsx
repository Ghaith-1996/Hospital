import React from "react";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
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
const cursor = "CQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJ";
const event = { id, action: "alert.confirmed", resourceType: "alert", resourceId: id,
  actorType: "user", actorUserId: id, outcome: "succeeded", correlationId: correlation,
  occurredAtUtc: "2026-09-19T12:00:00Z", metadata: { version: 2, channel: "Sms" } };
function respond(body: unknown, status = 200) { return Response.json(body, { status }); }

test("audit loading and empty states are accessible", async () => {
  let finish!: (value: Response) => void;
  vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>(resolve => { finish = resolve; })));
  render(<AuditEvents />);
  expect(screen.getByRole("status")).toHaveTextContent("Loading audit events");
  finish(respond({ events: [], nextCursor: null }));
  expect(await screen.findByText("No audit events match these filters.")).toBeVisible();
  expect(screen.getByRole("button", { name: "Next page" })).toBeDisabled();
});

test("safe audit events render as an accessible table with labelled UTC filters", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => respond({ events: [event], nextCursor: null })));
  render(<AuditEvents />);
  expect(await screen.findByRole("table", { name: "Audit events" })).toBeVisible();
  expect(screen.getByRole("columnheader", { name: "Correlation ID" })).toBeVisible();
  expect(screen.getByRole("cell", { name: "alert.confirmed" })).toBeVisible();
  expect(screen.getByText(/version: 2/)).toBeVisible();
  expect(screen.getByLabelText("From (UTC)")).toBeVisible();
  expect(screen.getByLabelText("To (UTC)")).toBeVisible();
  expect(screen.getByLabelText("Correlation ID")).toBeVisible();
});

test("filters are applied deliberately and next and previous use server cursors", async () => {
  const fetcher = vi.fn(async (path: string) => respond({ events: [event], nextCursor: path.includes("cursor=") ? null : cursor }));
  vi.stubGlobal("fetch", fetcher);
  render(<AuditEvents />);
  await screen.findByRole("table");
  fireEvent.change(screen.getByLabelText("Action"), { target: { value: "alert.confirmed" } });
  fireEvent.change(screen.getByLabelText("Outcome"), { target: { value: "succeeded" } });
  fireEvent.change(screen.getByLabelText("Resource type"), { target: { value: "alert" } });
  fireEvent.change(screen.getByLabelText("Correlation ID"), { target: { value: correlation } });
  expect(fetcher).toHaveBeenCalledTimes(1);
  fireEvent.submit(screen.getByRole("form", { name: "Audit filters" }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(2));
  expect(fetcher.mock.calls[1][0]).toContain("action=alert.confirmed");
  expect(fetcher.mock.calls[1][0]).toContain("correlationId=");
  fireEvent.click(screen.getByRole("button", { name: "Next page" }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(3));
  expect(fetcher.mock.calls[2][0]).toContain("cursor=" + cursor);
  await waitFor(() => expect(screen.getByRole("button", { name: "Previous page" })).toBeEnabled());
  fireEvent.click(screen.getByRole("button", { name: "Previous page" }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(4));
  expect(fetcher.mock.calls[3][0]).not.toContain("cursor=");
});

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

test.each([{}, { events: "wrong", nextCursor: null }, { events: [event], nextCursor: "invalid" }])(
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

test.each(["Auditor", "SystemAdministrator", "Operator", "Practitioner", "DirectoryAdministrator"])(
  "audit navigation follows the server role (%s)", role => {
    session.user.roles = [role];
    render(<AppShell><p>Simulation content</p></AppShell>);
    expect(screen.queryByRole("link", { name: "Audit" }) !== null).toBe(["Auditor", "SystemAdministrator"].includes(role));
  });
