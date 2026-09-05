import { expect, test } from "@playwright/test";

test("shows the simulation platform boundary", async ({ page }) => {
  await page.goto("/");

  await expect(page.getByRole("status", { name: "SIMULATION MODE" })).toBeVisible();
  await expect(page.getByRole("status", { name: "DEVELOPMENT AUTHENTICATION" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Critical Alerts Platform" })).toBeVisible();
  await expect(page.getByRole("button", { name: /dispatch/i })).toHaveCount(0);
  await expect(page.getByLabel("Simulation user")).toHaveCount(0);

  await page.goto("/alerts/new");
  await expect(page.getByRole("heading", { name: "Create typed simulation alert" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Create draft" })).toBeVisible();
  await expect(page.getByRole("button", { name: /dispatch/i })).toHaveCount(0);

  await page.goto("/directory");
  await expect(page.getByRole("heading", { name: "Fictional practitioner directory" })).toBeVisible();
  await expect(page.getByLabel("Search practitioners")).toBeVisible();
  await expect(page.getByRole("button", { name: /dispatch/i })).toHaveCount(0);
});

test("navigates the Phase 8 practitioner response and operator status routes", async ({ page }) => {
  const alertId = "00000000-0000-0000-0000-000000000801";
  const summary = {
    alertId,
    confirmedVersion: 3,
    state: "Active",
    location: "North Wing Simulation Room 8",
    urgencyLabel: "DEMO-URGENT",
    confirmedAtUtc: "2026-08-30T16:00:00Z",
    channels: ["SecureMessage", "Sms"],
    openedState: "PendingNotObserved",
    acknowledgedAtUtc: null,
    terminalDisposition: null,
    responsibilityAcceptedAtUtc: null,
  };

  await page.route("**/api/v1/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === "/api/v1/dev/identities" || path === "/api/v1/me") {
      await route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
      return;
    }
    if (path === "/api/v1/my-alerts") {
      await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify([summary]) });
      return;
    }
    if (path === `/api/v1/my-alerts/${alertId}/opened`) {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          alertId,
          confirmedVersion: 3,
          secureMessageOpenedAtUtc: "2026-08-30T16:01:00Z",
          replayed: false,
        }),
      });
      return;
    }
    if (path === `/api/v1/my-alerts/${alertId}/responses`) {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          alertId,
          confirmedVersion: 3,
          responseType: "Accepted",
          acknowledgedAtUtc: null,
          terminalDisposition: "Accepted",
          responsibilityAcceptedAtUtc: "2026-08-30T16:02:00Z",
          replayed: false,
        }),
      });
      return;
    }
    if (path === `/api/v1/my-alerts/${alertId}`) {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          ...summary,
          simulationPatientReference: "SIM-PAT-PHASE8",
          approvedMessage: "SIMULATION: fictional Phase 8 approved message",
          criticalFields: [{ fieldId: "heartRate", normalizedValue: "118", unit: "beats/min" }],
          secureMessageOpenedAtUtc: null,
        }),
      });
      return;
    }
    if (path === `/api/v1/alerts/${alertId}/live`) {
      await route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          alertId,
          confirmedVersion: 3,
          alertState: "Active",
          outboxState: "Processed",
          refreshedAtUtc: "2026-08-30T16:05:00Z",
          recipients: [{
            practitionerId: "00000000-0000-0000-0000-000000000108",
            simulationCode: "SIM-PRAC-0108",
            displayName: "Riley Sato",
            specialty: "Neurology",
            onCallSnapshot: "Primary",
            acknowledgedAtUtc: null,
            terminalDisposition: "Accepted",
            responsibilityAcceptedAtUtc: "2026-08-30T16:02:00Z",
            attempts: [{
              channel: "SecureMessage",
              attemptNumber: 1,
              status: "Delivered",
              openedState: "Occurred",
              openedAtUtc: "2026-08-30T16:01:00Z",
              requestedAtUtc: "2026-08-30T16:00:00Z",
              submittedAtUtc: "2026-08-30T16:00:20Z",
              deliveredAtUtc: "2026-08-30T16:00:40Z",
              failedAtUtc: null,
              failureCategory: null,
            }],
          }],
        }),
      });
      return;
    }
    await route.fulfill({ status: 404, contentType: "application/json", body: "{}" });
  });

  await page.goto("/my-alerts");
  await expect(page.getByRole("heading", { name: "My simulation alerts" })).toBeVisible();
  await page.getByRole("link", { name: "Open addressed alert" }).click();
  await expect(page.getByText("SIMULATION: fictional Phase 8 approved message")).toBeVisible();
  await expect(page.getByText(/SecureMessage opened at 2026-08-30T16:01:00.000Z/)).toBeVisible();
  await page.getByRole("button", { name: "Accept responsibility" }).click();
  await expect(page.getByText("Responsibility accepted. The alert remains active.")).toBeVisible();

  await page.goto(`/alerts/${alertId}/live`);
  await expect(page.getByRole("heading", { name: "Riley Sato" })).toBeVisible();
  await expect(page.getByText("This page polls for refreshed status; it is not guaranteed real-time monitoring.")).toBeVisible();
  await expect(page.getByText("Terminal disposition: Accepted")).toBeVisible();
});
