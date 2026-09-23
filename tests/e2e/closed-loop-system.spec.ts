import { execFileSync, spawn, type ChildProcess } from "node:child_process";
import { appendFileSync } from "node:fs";
import { expect, type APIRequestContext, type Page, test } from "@playwright/test";

const jordan = "Jordan Lee";
const riley = "Riley Sato";
const source = "SIMULATION: fictional patient has critical pulse 118 beats/min.";
const sbar = {
  situation: "SIMULATION: critical pulse requires review.",
  background: "SIMULATION: fictional background for system verification.",
  assessment: "SIMULATION: operator observed pulse 118 beats/min.",
  recommendation: "SIMULATION: review this secure alert.",
};

test.describe.serial("Phase 8.5 real closed loop", () => {
  test("A: browser completes durable dispatch, response, responsibility, and resolution", async ({ page }) => {
    await signIn(page, jordan);
    await page.goto("/alerts/new");
    await page.getByLabel("Simulation site").selectOption({ index: 1 });
    await page.getByLabel("Simulation department").selectOption({ index: 1 });
    await page.getByLabel("Fictional patient reference").fill("SIM-PAT-SYSTEM-A");
    await page.getByLabel("Fictional location").fill("North Wing / Simulation Room 204");
    await page.getByLabel("Operator-selected DEMO urgency").fill("Urgent");
    await page.getByLabel("Source text").fill(source);
    for (const [label, value] of Object.entries({ Situation: sbar.situation, Background: sbar.background, Assessment: sbar.assessment, Recommendation: sbar.recommendation })) {
      await page.getByLabel(label, { exact: true }).fill(value);
    }
    await page.getByRole("button", { name: "Add critical field" }).click();
    await page.getByLabel("Critical field 1 identifier").fill("heartRate");
    await page.getByLabel("Critical field 1 original value").fill("118");
    await page.getByLabel("Critical field 1 unit").fill("beats/min");
    await capture(page, "01-new-alert.png");
    await page.getByRole("button", { name: "Create backend draft" }).click();
    await expect(page).toHaveURL(/\/alerts\/[0-9a-f-]+\/compose$/);
    const alertId = page.url().match(/alerts\/([0-9a-f-]+)\/compose/i)![1];
    await capture(page, "02-compose-alert.png");

    await page.getByLabel("Approved secure message").fill("SIMULATION: secure clinical details for addressed fictional practitioners.");
    const messageSaved = page.waitForResponse((response) => response.request().method() === "PUT"
      && response.url().endsWith(`/api/v1/alerts/${alertId}/approved-message`));
    await page.getByRole("button", { name: "Approve and save message" }).click();
    const messageResponse = await messageSaved;
    expect(messageResponse.status()).toBe(200);
    const savedDraft = await messageResponse.json();
    await expect(page.getByText(`Draft version ${savedDraft.draftVersion} · ${savedDraft.state}`, { exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Select recipients and channels" }).click();
    await expect(page).toHaveURL(new RegExp(`/alerts/${alertId}/recipients$`));
    await page.getByLabel("Search name or specialty").fill("Riley");
    await page.getByRole("button", { name: "Search directory" }).click();
    const rileyRow = page.locator("li.clinician-row").filter({ hasText: "SIM-PRAC-0108" });
    await expect(rileyRow).toContainText("Riley Sato");
    await expect(rileyRow).toContainText(/Current|Synchronized/);
    await rileyRow.getByRole("checkbox").check();
    await page.getByLabel("Search name or specialty").fill("Maya");
    await page.getByRole("button", { name: "Search directory" }).click();
    const mayaRow = page.locator("li.clinician-row").filter({ hasText: "SIM-PRAC-0101" });
    await expect(mayaRow).toContainText("Maya Chen");
    await mayaRow.getByRole("checkbox").check();
    await page.getByLabel(/Channel for Riley Sato SIM-PRAC-0108/).selectOption("SecureMessage");
    await page.getByLabel(/Channel for Maya Chen SIM-PRAC-0101/).selectOption("SecureMessage");
    await expect(page.getByRole("heading", { name: "Selected Clinicians (2)" })).toBeVisible();
    await page.getByRole("button", { name: "Save recipients and reconfirm fields" }).click();

    await page.getByLabel("Approved value for heartRate").fill("118");
    await page.getByRole("button", { name: "Confirm heartRate value and unit" }).click();
    const criticalCard = page.locator("section.detail-card").filter({ has: page.getByRole("heading", { name: "heartRate" }) });
    await expect(criticalCard.getByText("Confirmed", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Submit for exact review" }).click();
    await expect(page.getByRole("heading", { name: "Exact Review" })).toBeVisible();
    await expect(page.getByText("SIM-PAT-SYSTEM-A")).toBeVisible();
    await expect(page.getByText(/Riley Sato/)).toBeVisible();
    await expect(page.getByText(/Channel: SecureMessage/).first()).toBeVisible();
    await capture(page, "03-exact-review.png");
    await page.getByRole("checkbox").check();
    const confirm = page.getByRole("button", { name: "Confirm & Dispatch" });
    await confirm.dblclick();
    await expect(page.getByRole("heading", { name: "DispatchQueued" })).toBeVisible();
    await page.getByRole("link", { name: "Open live status" }).click();
    await expect(page.getByRole("heading", { name: "Alert Live Status" })).toBeVisible();
    await expect(page.getByText("Status: Delivered").first()).toBeVisible({ timeout: 60_000 });
    await expect(page.locator("body")).toContainText("SIM-PRAC-0108");
    await capture(page, "04-live-delivery.png");

    await switchIdentity(page, riley);
    const inboxLink = page.getByRole("link", { name: new RegExp(`Open alert ${alertId}`, "i") });
    await expect(inboxLink).toBeVisible();
    await inboxLink.click();
    await expect(page.getByText("Opened: PendingNotObserved")).toBeVisible();
    await capture(page, "05-practitioner-alert.png");
    await page.getByRole("button", { name: "Record opened" }).click();
    await expect(page.getByText("Opened: Occurred")).toBeVisible();
    await expect(page.getByText("Acknowledged: Not recorded")).toBeVisible();
    await page.getByRole("button", { name: "Acknowledge" }).click();
    await expect(page.getByText(/Acknowledged: 20/)).toBeVisible();
    await expect(page.getByText("Responsibility accepted: Not recorded")).toBeVisible();
    await page.getByRole("button", { name: "Accept responsibility" }).click();
    await expect(page.getByText(/Responsibility accepted: 20/)).toBeVisible();

    await switchIdentity(page, jordan);
    await page.goto(`/alerts/${alertId}/live`);
    await expect(page.getByText("Status: Delivered").first()).toBeVisible();
    await expect(page.getByText(/Acknowledged:/).filter({ hasText: /20/ })).toBeVisible();
    await expect(page.getByText(/Responsibility accepted:/).filter({ hasText: /20/ })).toBeVisible();
    await page.getByRole("button", { name: "Resolve simulation alert" }).click();
    await expect(page.getByRole("heading", { name: "Resolved" })).toBeVisible();
    await page.reload();
    await expect(page.getByRole("heading", { name: "Resolved" })).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    await capture(page, "06-mobile-resolved-shell.png");
  });

  test("B: stale browser edit is rejected and current server version is reloaded", async ({ page }) => {
    await signIn(page, jordan);
    const draft = await createDraft(page.request, "SIM-PAT-SYSTEM-B");
    await page.goto("/alerts");
    await page.getByLabel("Alert identifier").fill(draft.alertId);
    await page.getByLabel("View").selectOption("compose");
    await page.getByRole("button", { name: "Open saved alert" }).click();
    await expect(page).toHaveURL(new RegExp(`/alerts/${draft.alertId}/compose$`));
    await expect(page.getByText(`Draft version ${draft.draftVersion}`)).toBeVisible();
    await page.getByLabel("Source text").fill("SIMULATION: stale browser edit must not overwrite.");
    const navigationDialog = page.waitForEvent("dialog");
    await page.evaluate(() => window.history.back());
    const dialog = await navigationDialog;
    expect(dialog.type()).toBe("confirm");
    await dialog.dismiss();
    await expect(page).toHaveURL(new RegExp(`/alerts/${draft.alertId}/compose$`));
    await expect(page.getByLabel("Source text")).toHaveValue("SIMULATION: stale browser edit must not overwrite.");
    const external = await apiJson(page.request, "patch", `/api/v1/alerts/${draft.alertId}`, {
      ...draftInput("SIM-PAT-SYSTEM-B"), expectedVersion: draft.draftVersion,
      sourceText: "SIMULATION: concurrent server edit must survive.",
    });
    await page.getByRole("button", { name: "Save source and SBAR" }).click();
    await expect(page.locator(".error-panel[role=alert]")).toContainText(/changed|reloaded|No stale edits/i);
    await expect(page.getByLabel("Source text")).toHaveValue("SIMULATION: stale browser edit must not overwrite.");
    await page.getByRole("button", { name: "Discard local edits and load server version" }).click();
    await expect(page.getByLabel("Source text")).toHaveValue("SIMULATION: concurrent server edit must survive.");
    await expect(page.getByText(`Draft version ${external.draftVersion}`)).toBeVisible();
    await expect(page.getByText("Confirmed", { exact: true })).toHaveCount(0);
    await page.getByLabel("Source text").fill("SIMULATION: deliberate edit discarded by accepted Back navigation.");
    const acceptedNavigation = page.waitForEvent("dialog");
    await page.evaluate(() => window.history.back());
    const acceptedDialog = await acceptedNavigation;
    expect(acceptedDialog.type()).toBe("confirm");
    await acceptedDialog.accept();
    await expect(page).toHaveURL(/\/alerts$/);
  });

  test("C: same confirmation key replays once and creates one logical delivery set", async ({ page }) => {
    await signIn(page, jordan);
    const prepared = await prepareConfirmableAlert(page.request, "SIM-PAT-SYSTEM-C");
    const key = `phase85-system-${crypto.randomUUID()}`;
    const review = await (await page.request.get(`/api/v1/alerts/${prepared.alertId}/review`)).json();
    const confirm = () => page.request.post(`/api/v1/alerts/${prepared.alertId}/confirm`, { headers: { "Idempotency-Key": key }, data: { expectedVersion: prepared.draftVersion, escalationPolicyId: review.escalationPlan.policyId, escalationPolicyVersion: review.escalationPlan.policyVersion, escalationPlanRevision: review.escalationPlan.revision } });
    const [first, replay] = await Promise.all([confirm(), confirm()]);
    expect(first.ok(), await first.text()).toBeTruthy();
    expect(replay.ok(), await replay.text()).toBeTruthy();
    const firstBody = await first.json();
    const replayBody = await replay.json();
    expect(firstBody).toMatchObject({ alertId: prepared.alertId, confirmedVersion: prepared.draftVersion, state: "DispatchQueued" });
    expect(replayBody).toMatchObject({ alertId: prepared.alertId, confirmedVersion: prepared.draftVersion, state: "DispatchQueued" });
    expect([firstBody.replayed, replayBody.replayed].sort()).toEqual([false, true]);
    await expect.poll(() => dbScalar(`select count(*) from delivery_attempts where alert_id = '${prepared.alertId}'`), { timeout: 60_000 }).toBe("1");
    expect(dbScalar(`select count(*) from outbox_messages where aggregate_id = '${prepared.alertId}'`)).toBe("1");
    expect(dbScalar(`select count(*) from alert_recipient_selections where alert_id = '${prepared.alertId}'`)).toBe("1");
  });
});

test.describe.serial("Phase 9 durable DEMO escalation", () => {
  const restarted: ChildProcess[] = [];
  let workers: number[] = [];
  test.beforeAll(() => {
    workers = requiredEnv("SYSTEM_E2E_WORKER_IDS").split(",").map(Number);
    expect(workers).toHaveLength(2);
    dbScalar(`INSERT INTO on_call_assignments
      (id, organization_id, practitioner_id, site_id, department_id, tier, starts_at_utc, ends_at_utc, source_system, source_record_id, last_synchronized_at_utc)
      VALUES (gen_random_uuid(), '11111111-1111-4111-8111-111111111111', '11111111-1111-4111-8111-111111110101',
      '11111111-1111-4111-8111-111111111201', '11111111-1111-4111-8111-111111110301', 'Backup',
      clock_timestamp() - interval '1 minute', clock_timestamp() + interval '1 day', 'SIM-SYSTEM-TEST', 'SIM-PHASE9-BACKUP', clock_timestamp())`);
  });
  test.afterAll(() => { for (const child of restarted) if (child.exitCode === null) child.kill(); });

  function restartWorkers() {
    for (const pid of workers) { try { process.kill(pid); } catch { /* Already exited owned test worker. */ } }
    workers = [0, 1].map(() => {
      const child = spawn(requiredEnv("SYSTEM_E2E_DOTNET"), [requiredEnv("SYSTEM_E2E_WORKER_DLL")], { stdio: "ignore", windowsHide: true });
      restarted.push(child);
      if (!child.pid) throw new Error("Could not restart the owned simulation worker.");
      appendFileSync(requiredEnv("SYSTEM_E2E_WORKER_LEDGER"), `${child.pid}\n`);
      return child.pid;
    });
  }

  async function confirmScenario(page: Page, name: string) {
    await signIn(page, jordan);
    const draft = await prepareConfirmableAlert(page.request, `SIM-PAT-SYSTEM-${name}`);
    const review = await apiJson(page.request, "get", `/api/v1/alerts/${draft.alertId}/review`);
    expect(review.escalationPlan.policyVersion).toBe("DEMO-9");
    expect(review.escalationPlan.steps[0].recipients.map((person: { displayName: string }) => person.displayName)).toEqual(["Maya Chen"]);
    const response = await page.request.post(`/api/v1/alerts/${draft.alertId}/confirm`, {
      headers: { "Idempotency-Key": `phase9-${crypto.randomUUID()}` }, data: { expectedVersion: draft.draftVersion,
        escalationPolicyId: review.escalationPlan.policyId, escalationPolicyVersion: review.escalationPlan.policyVersion,
        escalationPlanRevision: review.escalationPlan.revision } });
    expect(response.ok(), await response.text()).toBeTruthy();
    await expect.poll(() => dbScalar(`select state from escalation_runs where alert_id = '${draft.alertId}'`)).toBe("Scheduled");
    return draft;
  }

  async function respond(page: Page, draft: { alertId: string; draftVersion: number }, responseType: string) {
    await switchIdentity(page, riley);
    const response = await page.request.post(`/api/v1/my-alerts/${draft.alertId}/responses`, {
      headers: { "Idempotency-Key": `phase9-${crypto.randomUUID()}` }, data: { expectedVersion: draft.draftVersion, responseType } });
    expect(response.ok(), await response.text()).toBeTruthy();
    await switchIdentity(page, jordan);
  }

  async function expectBackup(draft: { alertId: string; draftVersion: number }) {
    const id = draft.alertId;
    await expect.poll(() => dbScalar(`select count(*) from delivery_attempts d join alert_recipient_selections s on s.id = d.recipient_selection_id
      where d.alert_id = '${id}' and s.selection_source = 'EscalationPolicy' and d.status = 'Delivered'`), { timeout: 75_000 }).toBe("1");
    expect(dbScalar(`select count(*) from alert_recipient_selections where alert_id = '${id}' and selection_source = 'EscalationPolicy'`)).toBe("1");
    expect(dbScalar(`select count(*) from outbox_messages where aggregate_id = '${id}' and event_type = 'EscalationDispatchRequested'`)).toBe("1");
    expect(dbScalar(`select count(*) from delivery_attempts where alert_id = '${id}'`)).toBe("2");
    expect(dbScalar(`select draft_version from alerts where id = '${id}'`)).toBe(String(draft.draftVersion));
  }

  test("D: database deadline activates and delivers the approved backup", async ({ page }) => {
    const draft = await confirmScenario(page, "D");
    await expectBackup(draft);
    await page.goto(`/alerts/${draft.alertId}/live`);
    await expect(page.getByText("Activated from the approved DEMO escalation plan")).toBeVisible();
    await expect(page.getByText(/Escalation state: Exhausted/)).toBeVisible();
    await capture(page, "07-escalation-exhausted.png");
  });

  test("E: acknowledgement alone does not stop deadline escalation", async ({ page }) => {
    const draft = await confirmScenario(page, "E");
    await respond(page, draft, "Acknowledged");
    await expectBackup(draft);
    expect(dbScalar(`select count(*) from responsibility_assignments where alert_id = '${draft.alertId}'`)).toBe("0");
  });

  test("F: accepted responsibility survives restart and prevents backup after deadline", async ({ page }) => {
    const draft = await confirmScenario(page, "F");
    await respond(page, draft, "Accepted");
    restartWorkers();
    await expect.poll(() => dbScalar(`select state from escalation_runs where alert_id = '${draft.alertId}'`)).toBe("Stopped");
    await expect.poll(() => dbScalar(`select clock_timestamp() > next_due_at_utc from escalation_runs where alert_id = '${draft.alertId}'`), { timeout: 75_000 }).toBe("t");
    expect(dbScalar(`select count(*) from alert_recipient_selections where alert_id = '${draft.alertId}' and selection_source = 'EscalationPolicy'`)).toBe("0");
    expect(dbScalar(`select count(*) from outbox_messages where aggregate_id = '${draft.alertId}' and event_type = 'EscalationDispatchRequested'`)).toBe("0");
  });

  for (const responseType of ["Declined", "Unavailable"]) test(`G: ${responseType} makes the approved step immediately eligible`, async ({ page }) => {
    const draft = await confirmScenario(page, `G-${responseType}`);
    await respond(page, draft, responseType);
    await expect.poll(() => dbScalar(`select count(*) from outbox_messages where aggregate_id = '${draft.alertId}' and event_type = 'EscalationDispatchRequested'`), { timeout: 15_000 }).toBe("1");
    await expectBackup(draft);
    expect(dbScalar(`select completed_at_utc < next_due_at_utc from escalation_runs where alert_id = '${draft.alertId}'`)).toBe("t");
  });

  test("H: pause survives worker restart and original deadline, then resume escalates once", async ({ page }) => {
    test.setTimeout(180_000);
    const draft = await confirmScenario(page, "H");
    await page.goto(`/alerts/${draft.alertId}/live`);
    await page.getByRole("button", { name: "Pause DEMO escalation" }).dblclick();
    await expect(page.getByText(/Escalation state: Paused/)).toBeVisible();
    restartWorkers();
    await expect.poll(() => dbScalar(`select clock_timestamp() > next_due_at_utc from escalation_runs where alert_id = '${draft.alertId}'`), { timeout: 75_000 }).toBe("t");
    expect(dbScalar(`select count(*) from alert_recipient_selections where alert_id = '${draft.alertId}' and selection_source = 'EscalationPolicy'`)).toBe("0");
    await page.reload();
    await expect(page.getByText(/Escalation state: Paused/)).toBeVisible();
    await capture(page, "08-escalation-paused-after-restart.png");
    await page.getByRole("button", { name: "Resume DEMO escalation" }).dblclick();
    await expectBackup(draft);
    expect(dbScalar(`select count(*) from escalation_events e join escalation_runs r on r.id = e.run_id where r.alert_id = '${draft.alertId}' and e.kind = 'Resumed'`)).toBe("1");
  });

  test("I: two running workers produce one activation, outbox and logical delivery", async ({ page }) => {
    restartWorkers();
    const draft = await confirmScenario(page, "I");
    await expectBackup(draft);
    expect(dbScalar(`select count(*) from escalation_runs where alert_id = '${draft.alertId}'`)).toBe("1");
    expect(dbScalar(`select count(*) from escalation_events e join escalation_runs r on r.id = e.run_id where r.alert_id = '${draft.alertId}' and e.kind = 'RecipientActivated'`)).toBe("1");
  });
});

async function signIn(page: Page, displayName: string) {
  await page.goto("/");
  await switchIdentity(page, displayName);
}

async function switchIdentity(page: Page, displayName: string) {
  await page.getByRole("button", { name: /Select simulation identity|Jordan Lee|Riley Sato/ }).click();
  await page.getByRole("menuitem", { name: new RegExp(displayName) }).click();
  await expect(page.getByRole("button", { name: new RegExp(displayName) })).toBeVisible();
}

function draftInput(patient: string) {
  return {
    siteId: "11111111-1111-4111-8111-111111111201",
    departmentId: "11111111-1111-4111-8111-111111110301",
    simulationPatientReference: patient,
    location: "North Wing / Simulation Room 204",
    urgencyLabel: "Urgent",
    sourceText: source,
    sbar,
    criticalFields: [{ fieldId: "heartRate", originalValue: "118", unit: "beats/min" }],
  };
}

async function apiJson(request: APIRequestContext, method: "get" | "post" | "put" | "patch", path: string, data?: unknown) {
  const response = await request[method](path, data === undefined ? undefined : { data });
  if (!response.ok()) throw new Error(`${method.toUpperCase()} ${path}: ${response.status()} ${await response.text()}`);
  return response.json();
}

async function createDraft(request: APIRequestContext, patient: string) {
  return apiJson(request, "post", "/api/v1/alerts/drafts", draftInput(patient));
}

async function prepareConfirmableAlert(request: APIRequestContext, patient: string) {
  let draft = await createDraft(request, patient);
  draft = await apiJson(request, "put", `/api/v1/alerts/${draft.alertId}/approved-message`, { expectedVersion: draft.draftVersion, approvedMessage: "SIMULATION: system replay secure message." });
  const people = await apiJson(request, "get", "/api/v1/directory/practitioners?q=Riley&includeInactive=false");
  const person = people.find((candidate: { simulationCode: string }) => candidate.simulationCode === "SIM-PRAC-0108");
  expect(person).toBeTruthy();
  draft = await apiJson(request, "put", `/api/v1/alerts/${draft.alertId}/recipients`, { expectedVersion: draft.draftVersion, recipients: [{ practitionerId: person.practitionerId, practitionerRoleId: person.practitionerRoleId, channel: "SecureMessage", directoryRevision: person.selectionRevision }] });
  draft = await apiJson(request, "post", `/api/v1/alerts/${draft.alertId}/field-confirmations`, { expectedVersion: draft.draftVersion, fieldId: "heartRate", originalValue: "118", normalizedValue: "118", unit: "beats/min" });
  draft = await apiJson(request, "post", `/api/v1/alerts/${draft.alertId}/submit-for-confirmation`, { expectedVersion: draft.draftVersion });
  return draft;
}

function dbScalar(sql: string): string {
  const container = requiredEnv("SYSTEM_E2E_POSTGRES_CONTAINER");
  const database = requiredEnv("SYSTEM_E2E_POSTGRES_DATABASE");
  const user = requiredEnv("SYSTEM_E2E_POSTGRES_USER");
  return execFileSync("docker", ["exec", container, "psql", "--tuples-only", "--no-align", "--username", user, "--dbname", database, "--command", sql], { encoding: "utf8", windowsHide: true }).trim();
}

function requiredEnv(name: string): string {
  const value = process.env[name];
  if (!value) throw new Error(`${name} is required; run through scripts/system-e2e.ps1.`);
  return value;
}

async function capture(page: Page, name: string) {
  const directory = process.env.SYSTEM_E2E_SCREENSHOT_DIR;
  if (directory) await page.screenshot({ path: `${directory}/${name}`, fullPage: true });
}
