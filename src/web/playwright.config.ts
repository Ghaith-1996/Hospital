import { defineConfig, devices } from "@playwright/test";

const webRoot = __dirname;
const e2eServer = {
  command: "node scripts/playwright-next-dev.mjs",
  cwd: webRoot,
  url: "http://127.0.0.1:3101",
  reuseExistingServer: false,
  timeout: 120_000,
} as const;

export default defineConfig({
  testDir: "../../tests/e2e",
  globalSetup: "./scripts/playwright-global-setup.mjs",
  globalTeardown: "./scripts/playwright-global-teardown.mjs",
  metadata: {
    e2eServer,
  },
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 2 : 0,
  reporter: "line",
  use: {
    baseURL: "http://127.0.0.1:3101",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
