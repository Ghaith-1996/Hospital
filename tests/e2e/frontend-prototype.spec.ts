import { expect, type Page, test } from "@playwright/test";

const storageKey = "critical-alerts.prototype.v1";

test("legacy live URLs open local prototype details without API requests", async ({ page }) => {
  const apiRequests: string[] = [];
  page.on("request", (request) => {
    if (new URL(request.url()).pathname.startsWith("/api/")) apiRequests.push(request.url());
  });
  await openWithFreshDemo(page, "/alerts/alert-critical-1/live");
  await expect(page).toHaveURL(/\/alerts\/alert-critical-1$/);
  await expect(page.getByRole("heading", { name: "Alert Details", exact: true })).toBeVisible();
  expect(apiRequests).toEqual([]);
});

test("drafts stay private until dispatch and confirmed alerts cannot be edited or dispatched twice", async ({ page }) => {
  await openWithFreshDemo(page, "/alerts/new");
  await page.getByLabel("Patient Reference").fill("SIM-PAT-BOUNDARY-001");
  await page.getByLabel("Case Details").fill("SIMULATION: fictional boundary regression.");
  await page.getByRole("button", { name: "Add Dr. Marc Tremblay", exact: true }).click();
  await page.getByRole("button", { name: "Review & Confirm", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
  const reviewUrl = page.url();
  const editUrl = await page.getByRole("link", { name: "Back/Edit" }).getAttribute("href");
  const alertId = reviewUrl.split("/").at(-2)!;

  await switchToMarc(page);
  await expect(page.getByText("SIM-PAT-BOUNDARY-001")).toHaveCount(0);
  await page.goto(`/my-alerts/${alertId}`);
  await expect(page.getByRole("heading", { name: "Fictional alert not found" })).toBeVisible();
  await page.goto(`/my-alerts/${alertId}/respond`);
  await expect(page.getByRole("heading", { name: "Fictional alert not found" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Submit Response" })).toHaveCount(0);

  await page.getByRole("button", { name: /Dr\. Marc Tremblay/ }).click();
  await page.getByRole("menuitem", { name: /Sophie Bernard/ }).click();
  await page.goto(reviewUrl);
  await page.getByRole("button", { name: "Confirm & Dispatch" }).click();
  await page.getByRole("button", { name: "Confirm fictional dispatch" }).click();
  await expect(page.getByRole("heading", { name: "Alert Sent Successfully!" })).toBeVisible();
  await page.goBack();
  await expect(page.getByRole("heading", { name: "This alert is no longer a draft" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Confirm & Dispatch" })).toHaveCount(0);
  await page.goto(editUrl!);
  await expect(page.getByRole("heading", { name: "This alert is no longer a draft" })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Case Details" })).toHaveCount(0);

  await page.getByRole("link", { name: "Alert Doctor", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Patient Reference" })).toHaveValue("");

  await switchToMarc(page);
  await expect(page.getByRole("row").filter({ hasText: "SIM-PAT-BOUNDARY-001" })).toContainText("Sent");
  await page.getByRole("link", { name: "Open boundary regression", exact: true }).click();
  await page.getByRole("button", { name: "Accept", exact: true }).click();
  await page.getByRole("button", { name: "Submit Response" }).click();
  await expect(page.getByText("Your current response: Accepted")).toBeVisible();
});

test("resolved alerts remain readable but cannot receive responses or reopen", async ({ page }) => {
  await selectStoredUser(page, "user-david", "/my-alerts");
  await page.getByRole("link", { name: "Open Post-op hypotension", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Post-op hypotension", exact: true })).toBeVisible();
  await expect(page.getByText("SIM-PAT-1004")).toBeVisible();
  await expect(page.getByText(/responses are closed/i)).toBeVisible();
  await expect(page.getByRole("button", { name: "Accept", exact: true })).toHaveCount(0);
  await page.goto("/my-alerts/alert-resolved-1/respond?response=accepted");
  await expect(page.getByRole("heading", { name: "Responses are closed" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Submit Response" })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole("heading", { name: "Responses are closed" })).toBeVisible();
  await page.getByRole("link", { name: "Back to Alert" }).click();
  await page.getByRole("link", { name: "Back to Inbox" }).click();
  await expect(page.getByRole("row").filter({ hasText: "SIM-PAT-1004" })).toContainText("Resolved");
});

async function openWithFreshDemo(page: Page, path: string) {
  await page.goto(path);
  await page.evaluate((key) => {
    window.localStorage.removeItem(key);
  }, storageKey);
  await page.goto(path);
}

async function switchToMarc(page: Page) {
  await page.getByRole("button", { name: /Sophie Bernard/ }).click();
  await page.getByRole("menuitem", { name: /Dr\. Marc Tremblay/ }).click();
  await expect(page.getByRole("heading", { name: "Inbox" })).toBeVisible();
}

async function expectNoHorizontalOverflow(page: Page) {
  await expect
    .poll(() =>
      page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    )
    .toBe(true);
}

async function selectStoredUser(page: Page, userId: string, path: string) {
  await openWithFreshDemo(page, "/alerts/new");
  await page.waitForFunction((key) => {
    const state = JSON.parse(window.localStorage.getItem(key) ?? "{}");
    return Array.isArray(state.alerts) && state.alerts.length > 0;
  }, storageKey);
  await page.evaluate(
    ({ key, selectedUserId }) => {
      const state = JSON.parse(window.localStorage.getItem(key) ?? "{}");
      state.selectedUserId = selectedUserId;
      window.localStorage.setItem(key, JSON.stringify(state));
    },
    { key: storageKey, selectedUserId: userId },
  );
  await page.goto(path);
}

test("operator creates, reviews, sends, and opens a fictional alert", async ({ page }) => {
  await openWithFreshDemo(page, "/alerts/new");
  await expect(page.getByRole("button", { name: "Close navigation" })).not.toBeVisible();
  await page.getByLabel("Patient Reference").fill("SIM-PAT-E2E-001");
  await page.getByLabel("Case Details").fill("SIMULATION: fictional E2E alert details.");
  await page.getByLabel("Search fictional clinicians").fill("Marc");
  await page.getByRole("button", { name: "Add Dr. Marc Tremblay" }).click();
  await page.getByRole("button", { name: "Review & Confirm" }).click();
  await expect(page.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
  await expect(page.getByText("SIM-PAT-E2E-001")).toBeVisible();
  await page.getByRole("button", { name: "Confirm & Dispatch" }).click();
  await page.getByRole("button", { name: "Confirm fictional dispatch" }).click();
  await expect(page.getByRole("heading", { name: "Alert Sent Successfully!" })).toBeVisible();
  await expect(page.getByText(/No real notification was sent/i)).toBeVisible();
  await page.getByRole("link", { name: "View Alert Details" }).click();
  await expect(page.getByRole("heading", { name: "Alert Details" })).toBeVisible();
  await expect(page.getByText("SIM-PAT-E2E-001")).toBeVisible();
  await expect(page.getByRole("region", { name: "Alert Information" }).getByText("Not observed")).toBeVisible();
});

test("Dr. Marc acknowledges an alert without accepting responsibility", async ({ page }) => {
  await openWithFreshDemo(page, "/alerts/new");
  await switchToMarc(page);

  await page.getByRole("link", { name: "Open Chest pain, hypotension" }).click();
  await expect(page.getByRole("heading", { name: "Chest pain, hypotension" })).toBeVisible();
  await page.getByRole("button", { name: "Acknowledge" }).click();
  await expect(page.getByRole("heading", { name: "Respond to Alert" })).toBeVisible();
  await expect(page.getByRole("radio", { name: "Acknowledge" })).toBeChecked();
  await page.getByRole("button", { name: "Submit Response" }).click();

  await expect(page.getByRole("status", { name: "Fictional response saved" })).toBeVisible();
  await expect(page.getByText("Your current response: Acknowledged")).toBeVisible();
  await expect(page.getByText("Acknowledgement recorded without accepting responsibility.")).toBeVisible();
  await expect(page.getByText("Responsibility accepted in this local simulation.")).toHaveCount(0);

  const marcRecipient = await page.evaluate((key) => {
    const state = JSON.parse(window.localStorage.getItem(key) ?? "{}");
    const alert = state.alerts?.find((candidate: { id: string }) => candidate.id === "alert-critical-1");
    return alert?.recipients?.find(
      (recipient: { clinicianId: string }) => recipient.clinicianId === "clinician-marc",
    );
  }, storageKey);

  expect(marcRecipient).toMatchObject({ response: "acknowledged" });
  expect(marcRecipient).not.toHaveProperty("responsibilityAcceptedAt");
});

test("operator filters and opens the fixed demo escalation without automatic step changes", async ({ page }) => {
  await openWithFreshDemo(page, "/alerts");
  await page.getByRole("button", { name: "Filters" }).click();
  await page.getByRole("region", { name: "Alert filters" }).getByLabel("Status").selectOption("escalating");
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(page.getByRole("link", { name: "Open SIM-PAT-1005" })).toBeVisible();
  await page.getByRole("link", { name: "Open SIM-PAT-1005" }).click();

  const escalationRegion = page.getByRole("region", { name: "Alert Escalation" });
  await expect(page.getByRole("heading", { name: "Alert Escalation" })).toBeVisible();
  await expect(page.getByText("DEMO elapsed time: 12 min")).toBeVisible();
  await expect(page.getByText("Escalating to fictional on-call cardiologist")).toBeVisible();
  await expect(escalationRegion.getByText("Complete")).toHaveCount(1);
  await expect(escalationRegion.getByText("In progress")).toHaveCount(1);
  await expect(escalationRegion.getByText("Pending")).toHaveCount(1);
  await expect(page.getByText(/next update in/i)).toHaveCount(0);

  await page.reload();
  await expect(page.getByText("DEMO elapsed time: 12 min")).toBeVisible();
  await expect(escalationRegion.getByText("In progress")).toHaveCount(1);
});

test("mobile navigation, alert creation, cards, and overflow remain usable at 390x844", async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 844 });
  await openWithFreshDemo(page, "/alerts/new");
  await expect
    .poll(() =>
      page.evaluate(() => getComputedStyle(document.querySelector(".new-alert-layout")!).gridTemplateColumns.split(" ").length),
    )
    .toBe(1);

  await page.setViewportSize({ width: 390, height: 844 });
  await openWithFreshDemo(page, "/alerts/new");

  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to content" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("#main-content")).toBeFocused();

  const menuButton = page.getByRole("button", { name: "Open navigation" });
  await expect(menuButton).toBeVisible();
  await expect(menuButton).toHaveAttribute("aria-expanded", "false");
  await expect(page.locator("#prototype-sidebar")).toHaveAttribute("aria-hidden", "true");
  await expect(page.getByRole("button", { name: "Close navigation" })).toHaveCount(0);
  await menuButton.click();
  await expect(menuButton).toHaveAttribute("aria-expanded", "true");
  await expect(page.locator("#prototype-sidebar")).not.toHaveAttribute("aria-hidden", "true");
  await expect(page.getByRole("navigation", { name: "Operator navigation" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Close navigation" })).toBeFocused();
  await expect(page.locator("#main-content")).toHaveJSProperty("inert", true);
  await page.getByRole("button", { name: "Close navigation" }).click();
  await expect(menuButton).toHaveAttribute("aria-expanded", "false");
  await expect(menuButton).toBeFocused();

  const longReference = `SIM-PAT-MOBILE-LONG-${"ABCDEFGH".repeat(12)}`;
  await page.getByLabel("Patient Reference").fill(longReference);
  await page.getByLabel("Case Details").fill("SIMULATION: fictional mobile alert details.");
  await page.getByLabel("Search fictional clinicians").fill("Marc");
  await page.getByRole("button", { name: "Add Dr. Marc Tremblay" }).click();
  await page.getByRole("button", { name: "Review & Confirm" }).click();
  await expect(page.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
  await expectNoHorizontalOverflow(page);

  await page.goto("/alerts");
  await expect(page.getByRole("table", { name: "Fictional alerts" })).not.toBeVisible();
  await expect(page.getByLabel("Fictional alert cards")).toBeVisible();
  const mobileCard = page.getByRole("article").filter({ hasText: longReference });
  await expect(mobileCard.getByText("Patient Reference")).toBeVisible();
  await expect(mobileCard.getByText("Urgency")).toBeVisible();
  await expect(mobileCard.getByText("Status")).toBeVisible();
  await expect(mobileCard.getByText("Recipients")).toBeVisible();
  await expect(mobileCard.getByText("Last Updated")).toBeVisible();
  await expectNoHorizontalOverflow(page);

  await selectStoredUser(page, "user-marc", "/my-alerts/alert-critical-1");
  await expect(page.getByRole("heading", { name: "Chest pain, hypotension" })).toBeVisible();
  await page.getByRole("region", { name: "Respond to this fictional alert" }).scrollIntoViewIfNeeded();
  await expect
    .poll(() =>
      page.evaluate(() => {
        const current = document.querySelector(".doctor-alert__current")?.getBoundingClientRect();
        const panel = document.querySelector(".response-panel")?.getBoundingClientRect();
        if (!current || !panel) return false;
        return panel.top >= current.bottom - 1 && panel.bottom <= window.innerHeight;
      }),
    )
    .toBe(true);
  await expectNoHorizontalOverflow(page);
});
