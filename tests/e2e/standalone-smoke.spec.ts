import { expect, test } from "@playwright/test";

test("standalone UI identifies simulation mode and fails closed without backend authentication", async ({ page }) => {
  await page.goto("/alerts/new");
  await expect(page.getByRole("status", { name: "SIMULATION MODE" })).toBeVisible();
  await expect(page.getByText("DEVELOPMENT AUTHENTICATION").first()).toBeVisible();
  await expect(page.getByRole("alert").filter({ hasText: /session unavailable/i })).toBeVisible();
  await expect(page.getByRole("button", { name: "Create backend draft" })).toHaveCount(0);
});
