import { execFileSync } from "node:child_process";
import { expect, type APIRequestContext, type Page } from "@playwright/test";
export const jordan = "Jordan Lee";
export const riley = "Riley Sato";
export const source = "SIMULATION: fictional patient has critical pulse 118 beats/min.";
export const sbar = {
  situation: "SIMULATION: critical pulse requires review.",
  background: "SIMULATION: fictional background for system verification.",
  assessment: "SIMULATION: operator observed pulse 118 beats/min.",
  recommendation: "SIMULATION: review this secure alert.",
};

export async function signIn(page: Page, displayName: string) {
  await page.goto("/");
  await switchIdentity(page, displayName);
}

export async function switchIdentity(page: Page, displayName: string) {
  await page.getByRole("button", { name: /Select simulation identity|Jordan Lee|Riley Sato/ }).click();
  await page.getByRole("menuitem", { name: new RegExp(displayName) }).click();
  await expect(page.getByRole("button", { name: new RegExp(displayName) })).toBeVisible();
}

export function draftInput(patient: string) {
  return {
    siteId: "11111111-1111-4111-8111-111111111201",
    departmentId: "11111111-1111-4111-8111-111111110301",
    simulationPatientReference: patient,
    location: "North Wing / Simulation Room 204",
    urgencyLabel: "Urgent",
    sourceText: source,
    sbar,
    criticalFields: [{ fieldId: "heartRate", originalValue: "118", unit: "beats/min" }],
  };
}

export async function apiJson(request: APIRequestContext, method: "get" | "post" | "put" | "patch", path: string, data?: unknown) {
  const response = await request[method](path, data === undefined ? undefined : { data });
  if (!response.ok()) throw new Error(`${method.toUpperCase()} ${path}: ${response.status()} ${await response.text()}`);
  return response.json();
}

export async function createDraft(request: APIRequestContext, patient: string) {
  return apiJson(request, "post", "/api/v1/alerts/drafts", draftInput(patient));
}

export async function prepareConfirmableAlert(request: APIRequestContext, patient: string) {
  let draft = await createDraft(request, patient);
  draft = await apiJson(request, "put", `/api/v1/alerts/${draft.alertId}/approved-message`, { expectedVersion: draft.draftVersion, approvedMessage: "SIMULATION: system replay secure message." });
  const people = await apiJson(request, "get", "/api/v1/directory/practitioners?q=Riley&includeInactive=false");
  const person = people.find((candidate: { simulationCode: string }) => candidate.simulationCode === "SIM-PRAC-0108");
  expect(person).toBeTruthy();
  draft = await apiJson(request, "put", `/api/v1/alerts/${draft.alertId}/recipients`, { expectedVersion: draft.draftVersion, recipients: [{ practitionerId: person.practitionerId, practitionerRoleId: person.practitionerRoleId, channel: "SecureMessage", directoryRevision: person.selectionRevision }] });
  draft = await apiJson(request, "post", `/api/v1/alerts/${draft.alertId}/field-confirmations`, { expectedVersion: draft.draftVersion, fieldId: "heartRate", originalValue: "118", normalizedValue: "118", unit: "beats/min" });
  draft = await apiJson(request, "post", `/api/v1/alerts/${draft.alertId}/submit-for-confirmation`, { expectedVersion: draft.draftVersion });
  return draft;
}

export function dbScalar(sql: string): string {
  const container = requiredEnv("SYSTEM_E2E_POSTGRES_CONTAINER");
  const database = requiredEnv("SYSTEM_E2E_POSTGRES_DATABASE");
  const user = requiredEnv("SYSTEM_E2E_POSTGRES_USER");
  return execFileSync("docker", ["exec", container, "psql", "--tuples-only", "--no-align", "--username", user, "--dbname", database, "--command", sql], { encoding: "utf8", windowsHide: true }).trim();
}

export function requiredEnv(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required; run through scripts/system-e2e.ps1.`);
  return value;
}

export async function capture(page: Page, name: string) {
  const directory = process.env.SYSTEM_E2E_SCREENSHOT_DIR;
  if (directory) await page.screenshot({ path: `${directory}/${name}`, fullPage: true });
}
