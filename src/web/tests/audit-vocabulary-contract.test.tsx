import React from "react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { cleanup, render, screen, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { AuditEvents } from "../features/connected/audit-events";
import { auditActions, auditOutcomes, auditResourceTypes, queryAudit } from "../lib/audit";

// F43: the backend AuditSafety projection is the authoritative audit vocabulary. Every value it can return must
// cross the real fetch -> decode -> render boundary, and the client lists must not drift from it again.
// Vitest runs from src/web (its config root).
const source = readFileSync(resolve(process.cwd(), "../backend/CriticalAlerts.Application/Audit/AuditSafety.cs"), "utf8");
function backendSet(pattern: RegExp): string[] {
  const block = pattern.exec(source)?.[1];
  if (!block) throw new Error(`Backend audit vocabulary not found: ${pattern}`);
  return [...block.matchAll(/"([^"]+)"/g)].map(match => match[1]);
}
const backend = {
  actions: backendSet(/IReadOnlySet<string> Actions \{ get; \} = new\[\]\s*\{([\s\S]*?)\}\.ToFrozenSet/),
  outcomes: backendSet(/IReadOnlySet<string> Outcomes \{ get; \} = new\[\]\s*\{([\s\S]*?)\}\.ToFrozenSet/),
  resourceTypes: backendSet(/IReadOnlySet<string> ResourceTypes \{ get; \} = new\[\]\s*\{([\s\S]*?)\}\.ToFrozenSet/),
  actors: backendSet(/FrozenSet<string> Actors = new\[\] \{([\s\S]*?)\}\.ToFrozenSet/),
};
const id = "11111111-1111-4111-8111-111111111111";
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
function row(overrides: Record<string, string>) {
  return { id, action: "alert.confirmed", resourceType: "alert", resourceId: id, actorType: "user", outcome: "succeeded",
    correlationId: null, occurredAtUtc: "2026-10-07T02:00:00Z", metadata: {}, ...overrides };
}
function serve(events: unknown[]) {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ events, nextCursor: null })));
}

test("the backend vocabulary was found and is non-trivial", () => {
  expect(backend.actions.length).toBeGreaterThanOrEqual(40);
  expect(backend.actions).toContain("transcription.completed");
  expect(backend.actors).toContain("provider-webhook");
});

test("client vocabularies equal the backend AuditSafety vocabularies", () => {
  expect([...auditActions].sort()).toEqual([...backend.actions].sort());
  expect([...auditOutcomes].sort()).toEqual([...backend.outcomes].sort());
  expect([...auditResourceTypes].sort()).toEqual([...backend.resourceTypes].sort());
});

test.each([
  ...backend.actions.map(value => ["action", value]),
  ...backend.outcomes.map(value => ["outcome", value]),
  ...backend.resourceTypes.map(value => ["resourceType", value]),
  ...backend.actors.map(value => ["actorType", value]),
])("backend %s %s decodes through the real client", async (field, value) => {
  serve([row({ [field]: value })]);
  const page = await queryAudit({});
  expect(page.events).toHaveLength(1);
  expect(page.events[0][field as "action"]).toBe(value);
});

test("a page holding every backend action renders, including Phase 11 assistance and ACS webhook events", async () => {
  serve(backend.actions.map((action, index) => row({ action, id: `11111111-1111-4111-8111-${String(index).padStart(12, "0")}` })));
  render(<AuditEvents />);
  // Each action also appears as a filter <option>; assert the rendered audit records themselves.
  const table = await screen.findByRole("table", { name: "Audit events" });
  for (const action of backend.actions) expect(await within(table).findByText(action)).toBeVisible();
  expect(screen.queryByRole("alert")).toBeNull();
});

test.each([["action", "transcription.unknown"], ["actorType", "attacker"], ["outcome", "maybe"], ["resourceType", "patient"]])(
  "values outside the backend vocabulary are still rejected (%s %s)", async (field, value) => {
    serve([row({ [field]: value })]);
    await expect(queryAudit({})).rejects.toThrow("Audit response was invalid");
  });
