import { expect, type Page, test } from "@playwright/test";

const storageKey = "critical-alerts.prototype.v1";

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

test("operator creates, reviews, sends, and opens a fictional alert", async ({ page }) => {
  await openWithFreshDemo(page, "/alerts/new");
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
  await page.setViewportSize({ width: 390, height: 844 });
  await openWithFreshDemo(page, "/alerts/new");

  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to content" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("#main-content")).toBeFocused();

  const menuButton = page.getByRole("button", { name: "Open navigation" });
  await expect(menuButton).toBeVisible();
  await expect(menuButton).toHaveAttribute("aria-expanded", "false");
  await menuButton.click();
  await expect(menuButton).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("navigation", { name: "Operator navigation" })).toBeVisible();
  await page.getByRole("button", { name: "Close navigation" }).click();
  await expect(menuButton).toHaveAttribute("aria-expanded", "false");
  await expect(menuButton).toBeFocused();

  await page.getByLabel("Patient Reference").fill("SIM-PAT-MOBILE-001");
  await page.getByLabel("Case Details").fill("SIMULATION: fictional mobile alert details.");
  await page.getByLabel("Search fictional clinicians").fill("Marc");
  await page.getByRole("button", { name: "Add Dr. Marc Tremblay" }).click();
  await page.getByRole("button", { name: "Review & Confirm" }).click();
  await expect(page.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
  await expectNoHorizontalOverflow(page);

  await page.goto("/alerts");
  await expect(page.getByRole("table", { name: "Fictional alerts" })).not.toBeVisible();
  await expect(page.getByLabel("Fictional alert cards")).toBeVisible();
  const mobileCard = page.getByRole("article").filter({ hasText: "SIM-PAT-MOBILE-001" });
  await expect(mobileCard.getByText("Patient Reference")).toBeVisible();
  await expect(mobileCard.getByText("Urgency")).toBeVisible();
  await expect(mobileCard.getByText("Status")).toBeVisible();
  await expect(mobileCard.getByText("Recipients")).toBeVisible();
  await expect(mobileCard.getByText("Last Updated")).toBeVisible();
  await expectNoHorizontalOverflow(page);
});
