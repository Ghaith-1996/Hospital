import { expect, test } from "@playwright/test";
import { apiJson, createDraft, dbScalar, jordan, signIn } from "./system-helpers";

if (process.env.SYSTEM_E2E_ASSISTANCE !== "true") {
  test("Phase11: disabled assistance preserves typed compose", async ({ page }) => {
    await signIn(page, jordan);
    const draft = await createDraft(page.request, "SIM-PAT-ASSISTANCE-OFF");
    await page.goto(`/alerts/${draft.alertId}/compose`);
    expect((await apiJson(page.request, "get", "/api/v1/capabilities")).speechTranscription).toBe(false);
    await expect(page.getByLabel("Source text")).toBeEditable();
    await expect(page.getByRole("button", { name: "Suggest SBAR structure" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Record dictation" })).toHaveCount(0);
    await page.getByLabel("Source text").fill("SIMULATION: manual source remains primary");
    await page.getByRole("button", { name: "Save source and SBAR" }).click();
    await expect(page.getByText("Draft version 2 · Draft")).toBeVisible();
  });
} else {
  test("Phase11: transcription and evidence require separate explicit Apply", async ({ page }) => {
    await signIn(page, jordan);
    const draft = await createDraft(page.request, "SIM-PAT-ASSISTANCE-APPLY");
    await page.goto(`/alerts/${draft.alertId}/compose`);
    await page.getByRole("button", { name: "Use fictional dictation sample" }).press("Enter");
    await expect(page.getByRole("heading", { name: "Transcription suggestion" })).toBeVisible();
    await expect(page.getByLabel("Source text")).toHaveValue(draft.sourceText);
    await expect(page.getByText("Draft version 1 · Draft")).toBeVisible();
    await page.getByRole("button", { name: "Use transcript as source" }).press("Enter");
    await expect(page.getByText("Draft version 2 · Draft")).toBeVisible();
    await expect(page.getByLabel("Source text")).toHaveValue(/82\/54 mmHg/);
    await page.getByRole("button", { name: "Suggest SBAR structure" }).press("Enter");
    await expect(page.getByRole("heading", { name: "Source used for this suggestion" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Structured suggestion", exact: true })).toBeVisible();
    await expect(page.getByText("Confidence not provided").first()).toBeVisible();
    await expect(page.locator("mark").first()).toContainText("82/54 mmHg");
    await page.setViewportSize({ width: 390, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.getByRole("button", { name: "Apply evidence-backed suggestion" }).press("Enter");
    await expect(page.getByText("Draft version 3 · Draft")).toBeVisible();
    const applied = await apiJson(page.request, "get", `/api/v1/alerts/${draft.alertId}`);
    expect(applied.urgencyLabel).toBe(draft.urgencyLabel);
    expect(applied.recipients).toEqual(draft.recipients);
    expect(applied.approvedMessage).toBe(draft.approvedMessage);
    expect(applied.criticalFields.every((field: { status: string }) => field.status === "Unresolved")).toBe(true);
    expect(applied.criticalFields.some((field: { originalValue: string; unit: string }) => field.originalValue === "82/54" && field.unit === "mmHg")).toBe(true);
    expect(dbScalar(`select count(*) from outbox_messages where aggregate_id='${draft.alertId}'`)).toBe("0");
    expect(await page.evaluate(() => ({ local: localStorage.length, session: sessionStorage.length }))).toEqual({ local: 0, session: 0 });
    expect(await page.evaluate(async () => (await indexedDB.databases()).length)).toBe(0);
    expect(await page.evaluate(async () => (await caches.keys()).length)).toBe(0);
  });

  test("Phase11: stale result and provider outage retain manual editing", async ({ page }) => {
    await signIn(page, jordan);
    const draft = await createDraft(page.request, "SIM-PAT-ASSISTANCE-FALLBACK");
    await page.goto(`/alerts/${draft.alertId}/compose`);
    await page.getByRole("button", { name: "Use fictional dictation sample" }).click();
    await expect(page.getByRole("heading", { name: "Transcription suggestion" })).toBeVisible();
    await apiJson(page.request, "patch", `/api/v1/alerts/${draft.alertId}`, { ...draft, expectedVersion: 1,
      sourceText: "SIMULATION: manual concurrent update", criticalFields: [] });
    await page.getByRole("button", { name: "Use transcript as source" }).click();
    await expect(page.getByRole("region", { name: "Optional speech and structuring assistance" }).getByRole("alert")).toContainText("draft changed");
    await expect(page.getByRole("button", { name: "Use transcript as source" })).toBeDisabled();
    await page.reload();
    await expect(page.getByLabel("Source text")).toHaveValue("SIMULATION: manual concurrent update");
    await page.getByLabel("Fictional dictation scenario").selectOption("provider-outage");
    await page.getByRole("button", { name: "Use fictional dictation sample" }).click();
    await expect(page.getByRole("region", { name: "Optional speech and structuring assistance" }).getByRole("alert")).toContainText("continue with manual editing");
    await page.getByLabel("Source text").fill("SIMULATION: manually recovered after unavailable assistance");
    await expect(page.getByRole("button", { name: "Suggest SBAR structure" })).toBeDisabled();
    await page.getByRole("button", { name: "Save source and SBAR" }).click();
    await expect(page.getByText("Draft version 3 · Draft")).toBeVisible();
    expect(dbScalar(`select count(*) from alert_assistance_results where alert_id='${draft.alertId}'`)).toBe("1");
    expect(dbScalar(`select count(*) from outbox_messages where aggregate_id='${draft.alertId}'`)).toBe("0");
  });
}


