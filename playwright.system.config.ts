import { defineConfig, devices } from "@playwright/test";

const port = process.env.SYSTEM_E2E_WEB_PORT ?? "3111";

export default defineConfig({
  testDir: "tests/e2e",
  testMatch: "closed-loop-system.spec.ts",
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  reporter: "line",
  timeout: 120_000,
  expect: { timeout: 20_000 },
  use: { baseURL: `http://127.0.0.1:${port}`, trace: "retain-on-failure" },
  projects: [{ name: "chromium-system", use: { ...devices["Desktop Chrome"] } }],
});
