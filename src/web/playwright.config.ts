import { defineConfig, devices } from "@playwright/test";

const webRoot = __dirname;

export default defineConfig({
  testDir: "../../tests/e2e",
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
  webServer: {
    command: "node node_modules/next/dist/bin/next dev --hostname 127.0.0.1 --port 3101",
    cwd: webRoot,
    url: "http://127.0.0.1:3101",
    reuseExistingServer: true,
    timeout: 120_000,
    env: {
      ...process.env,
      CRITICAL_ALERTS_API_URL: "",
    },
  },
});
