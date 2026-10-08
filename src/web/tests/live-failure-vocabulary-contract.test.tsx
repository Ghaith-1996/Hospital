import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, expect, test, vi } from "vitest";
import { getAlertLive, liveFailureCategories } from "../lib/alerts";

// V31: the backend live projection's failure categories are authoritative. Every value it can return, including the
// Phase 12 voice categories, must decode through the real client, and the client list must not drift from it.
// Vitest runs from src/web (its config root).
const source = readFileSync(resolve(process.cwd(), "../backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs"), "utf8");
const block = /SafeFailureCategory\(string value\)[\s\S]*?return value switch\s*\{([\s\S]*?)=> value,/.exec(source)?.[1];
if (!block) throw new Error("Backend live failure categories not found.");
const backend = [...block.matchAll(/"([^"]+)"/g)].map(match => match[1]);
const alertId = "11111111-1111-4111-8111-111111111111";
afterEach(() => vi.unstubAllGlobals());

function live(failureCategory: string) {
  return {
    alertId, confirmedVersion: 3, alertState: "Active", outboxState: "Failed", refreshedAtUtc: "2026-10-08T12:00:00Z",
    canResolve: false, canCancel: true, manualFallbackRequired: true, escalation: null, operationalWarnings: [],
    recipients: [{
      practitionerId: "22222222-2222-4222-8222-222222222222", simulationCode: "SIM-PRAC-0102", displayName: "Fictional Rowan Patel",
      specialty: "Medicine", onCallSnapshot: null, acknowledgedAtUtc: null, terminalDisposition: null,
      responsibilityAcceptedAtUtc: null, callUnitRequestedAtUtc: null, lastResponseReasonCode: null, selectionSources: null,
      attempts: [{
        channel: "Voice", attemptNumber: 1, status: "Failed", openedState: "NotApplicable", openedAtUtc: null,
        requestedAtUtc: "2026-10-08T11:59:00Z", submittedAtUtc: "2026-10-08T11:59:01Z", deliveredAtUtc: null,
        failedAtUtc: "2026-10-08T11:59:40Z", failureCategory,
      }],
    }],
  };
}

test("the backend vocabulary was found and includes the voice categories", () => {
  expect(backend.length).toBeGreaterThanOrEqual(30);
  for (const category of ["voice-no-answer", "voice-busy", "voice-playback-incomplete", "call-outcome-unconfirmed"])
    expect(backend).toContain(category);
});

test("the client failure categories equal the backend live projection categories", () => {
  expect([...liveFailureCategories].sort()).toEqual([...backend].sort());
});

test.each(backend)("backend failure category %s decodes through the real client", async category => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json(live(category))));
  const result = await getAlertLive(alertId);
  expect(result.recipients[0].attempts[0].failureCategory).toBe(category);
  expect(result.recipients[0].attempts[0].status).toBe("Failed");
});

test("an unknown failure category is still rejected", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json(live("voice-voicemail-detected"))));
  await expect(getAlertLive(alertId)).rejects.toThrow("The live status response is invalid");
});
