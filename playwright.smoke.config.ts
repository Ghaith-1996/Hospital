import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "tests/e2e",
  testMatch: "standalone-smoke.spec.ts",
  globalSetup: "./src/web/scripts/playwright-global-setup.mjs",
  globalTeardown: "./src/web/scripts/playwright-global-teardown.mjs",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "line",
  use: { baseURL: "http://127.0.0.1:3101", trace: "on-first-retry" },
  projects: [{ name: "chromium-smoke", use: { ...devices["Desktop Chrome"] } }],
});
