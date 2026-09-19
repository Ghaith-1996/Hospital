import { expect, test, type APIRequestContext } from "@playwright/test";
import { apiJson, capture, dbScalar, jordan, prepareConfirmableAlert, riley, signIn, switchIdentity } from "./system-helpers";

async function command(request: APIRequestContext, path: string, data: unknown) {
  const key = crypto.randomUUID();
  for (let attempt = 0; attempt < 40; attempt++) {
    const response = await request.post(path, { headers: { "Idempotency-Key": key }, data });
    if (response.status() === 409 && attempt < 39) { await new Promise(resolve => setTimeout(resolve, 100)); continue; }
    expect(response.status()).toBe(200);
    return;
  }
}

test("J: authorized audit and safe operational recovery evidence", async ({ page }) => {
  await signIn(page, jordan);
  const alert = await prepareConfirmableAlert(page.request, "SIM-PAT-PHASE10-SENTINEL");
  const review = await apiJson(page.request, "get", `/api/v1/alerts/${alert.alertId}/review`);
  await command(page.request, `/api/v1/alerts/${alert.alertId}/confirm`, {
    expectedVersion: alert.draftVersion,
    escalationPolicyId: review.escalationPlan.policyId,
    escalationPolicyVersion: review.escalationPlan.policyVersion,
    escalationPlanRevision: review.escalationPlan.revision,
  });
  await expect.poll(() => dbScalar(`select count(*) from delivery_attempts where alert_id='${alert.alertId}' and status='Delivered'`), { timeout: 60_000 }).toBe("1");
  await expect.poll(() => dbScalar(`select count(*) from escalation_runs where alert_id='${alert.alertId}'`)).toBe("1");
  // Existing isolated-harness technique: make one exact confirmed step due, without changing policy or recipients.
  dbScalar(`update escalation_runs set next_due_at_utc=clock_timestamp()-interval '1 second' where alert_id='${alert.alertId}'`);
  await expect.poll(() => dbScalar(`select count(*) from audit_events where action='escalation-recipients-activated' and organization_id='11111111-1111-4111-8111-111111111111'`), { timeout: 60_000 }).not.toBe("0");
  await switchIdentity(page, riley);
  await command(page.request, `/api/v1/my-alerts/${alert.alertId}/responses`, { expectedVersion: alert.draftVersion, responseType: "Accepted" });
  await switchIdentity(page, jordan);
  await command(page.request, `/api/v1/alerts/${alert.alertId}/resolve`, { expectedVersion: alert.draftVersion });
  expect(await page.getByRole("link", { name: "Audit", exact: true }).count()).toBe(0);
  expect((await page.request.get("/api/v1/admin/audit")).status()).toBe(403);

  await switchIdentity(page, "Avery Auditor");
  await page.getByRole("link", { name: "Audit", exact: true }).click();
  await expect(page.getByRole("table", { name: "Audit events" })).toBeVisible();
  for (const action of ["alert.confirmed", "recipient.response.accepted", "alert.resolved", "escalation-recipients-activated"]) {
    await page.getByLabel("Action", { exact: true }).selectOption(action);
    await page.getByRole("button", { name: "Apply filters" }).press("Enter");
    await expect(page.getByRole("cell", { name: action, exact: true }).first()).toBeVisible();
  }
  const body = await page.locator("body").innerText();
  expect(body.includes("SIM-PAT-PHASE10-SENTINEL")).toBe(false);
  expect(body.includes("system replay secure message")).toBe(false);
  await capture(page, "phase10-safe-audit.png");

  // Real audited reads supply enough events to prove bounded cursor navigation.
  for (let index = 0; index < 51; index++) expect((await page.request.get("/api/v1/admin/audit?pageSize=1")).status()).toBe(200);
  await page.getByLabel("Action", { exact: true }).selectOption("");
  await page.getByRole("button", { name: "Apply filters" }).press("Enter");
  await expect(page.getByRole("status", { name: "Audit query status" })).toContainText("50 events");
  await page.getByRole("button", { name: "Next page" }).press("Enter");
  await expect(page.getByRole("status", { name: "Audit query status" })).toContainText("Page 2");
  await expect(page.getByRole("status", { name: "Audit query status" })).toBeFocused();
  await page.getByRole("button", { name: "Previous page" }).press("Enter");
  await expect(page.getByRole("status", { name: "Audit query status" })).toContainText("Page 1");

  await switchIdentity(page, "Morgan Ellis");
  await apiJson(page.request, "put", "/api/v1/dev/dispatch-scenarios/SecureMessage", { scenario: "ProviderOutage" });
  try {
    await switchIdentity(page, jordan);
    const failed = await prepareConfirmableAlert(page.request, "SIM-PAT-PHASE10-FAILURE");
    const failedReview = await apiJson(page.request, "get", `/api/v1/alerts/${failed.alertId}/review`);
    await command(page.request, `/api/v1/alerts/${failed.alertId}/confirm`, {
      expectedVersion: failed.draftVersion,
      escalationPolicyId: failedReview.escalationPlan.policyId,
      escalationPolicyVersion: failedReview.escalationPlan.policyVersion,
      escalationPlanRevision: failedReview.escalationPlan.revision,
    });
    await page.goto(`/alerts/${failed.alertId}/live`);
    await expect(page.getByRole("heading", { name: "Provider unavailable" })).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText(/Do not create a duplicate alert/).first()).toBeVisible();
    await expect(page.getByText(/REQUIRES_HOSPITAL_DECISION/).first()).toBeVisible();
    expect((await page.locator("body").innerText()).includes("SIM-PAT-PHASE10-FAILURE")).toBe(false);
    expect(dbScalar(`select count(*) from alerts where id='${failed.alertId}'`)).toBe("1");
    expect(Number(dbScalar(`select max(attempt_number) from delivery_attempts where alert_id='${failed.alertId}'`))).toBeLessThanOrEqual(2);
  } finally {
    await switchIdentity(page, "Morgan Ellis");
    expect((await page.request.delete("/api/v1/dev/dispatch-scenarios/SecureMessage")).status()).toBe(204);
  }
});
