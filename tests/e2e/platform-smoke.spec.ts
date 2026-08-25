import { expect, test } from "@playwright/test";

test("shows the simulation platform boundary", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("status", { name: "SIMULATION MODE" })).toBeVisible();
  await expect(page.getByRole("status", { name: "DEVELOPMENT AUTHENTICATION" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Critical Alerts Platform" })).toBeVisible();
  await expect(page.getByRole("button", { name: /dispatch/i })).toHaveCount(0);
  await expect(page.getByLabel("Simulation user")).toHaveCount(0);

  await page.goto("/directory");
  await expect(page.getByRole("heading", { name: "Fictional practitioner directory" })).toBeVisible();
  await expect(page.getByLabel("Search practitioners")).toBeVisible();
  await expect(page.getByRole("button", { name: /dispatch/i })).toHaveCount(0);
});
