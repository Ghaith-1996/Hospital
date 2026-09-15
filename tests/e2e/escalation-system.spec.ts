import { readFile } from "node:fs/promises";
import { setTimeout as delay } from "node:timers/promises";
import { expect, test, type APIRequestContext, type Page } from "@playwright/test";
import { apiJson, capture, createDraft, dbScalar, jordan, prepareConfirmableAlert, requiredEnv, riley, signIn, switchIdentity } from "./system-helpers";
import { writeControlJson } from "../../scripts/system-control-files.mjs";

type Prepared = { alertId: string; draftVersion: number };
type Workers = { id: string; generation: number; pids: number[]; dispatchEnabled: boolean; escalationEnabled: boolean; error?: string };
async function workers(command: "one" | "two" | "stop"): Promise<Workers> {
  const directory = requiredEnv("SYSTEM_E2E_WORKER_CONTROL");
  const id = crypto.randomUUID();
  await writeControlJson(directory, "request", { id, command });
  let result!: Workers;
  await expect.poll(async () => {
    result = JSON.parse(await readFile(`${directory}/status.json`, "utf8"));
    if (result.error) throw new Error(result.error);
    return result.id;
  }).toBe(id);
  expect(result.pids).toHaveLength(command === "two" ? 2 : command === "one" ? 1 : 0);
  expect(result.dispatchEnabled && result.escalationEnabled).toBeTruthy();
  for (const pid of result.pids) process.kill(pid, 0);
  return result;
}

// Only exact safe clock-conflict responses may retry the unchanged positive fixture command.
async function command(request: APIRequestContext, path: string, data: unknown) {
  const key = `phase9-system-${crypto.randomUUID()}`;
  const deadline = Date.now() + 5_000;
  while (true) {
    const response = await request.post(path, { headers: { "Idempotency-Key": key }, data });
    const body = await response.json();
    // RecordAsync emits response-conflict only for its database-clock guard; other disposition conflicts have distinct codes.
    const clockConflict = body.detail === "escalation-clock-conflict"
      || (path.endsWith("/responses") && body.detail === "response-conflict");
    if (response.status() === 409 && clockConflict && Date.now() < deadline) {
      await delay(50); continue;
    }
    expect(response.ok(), JSON.stringify(body)).toBeTruthy();
    return body;
  }
}
const where = (id: string) => `alert_id = '${id}'`;
const backups = (id: string) => dbScalar(`select count(*) from alert_recipient_selections where ${where(id)} and selection_source = 'EscalationPolicy'`);
const stepOutboxes = (id: string) => dbScalar(`select count(*) from outbox_messages where aggregate_id = '${id}' and event_type = 'EscalationDispatchRequested'`);
function due(id: string, seconds = 0) {
  dbScalar(`update escalation_runs set next_due_at_utc = clock_timestamp() + interval '${seconds} seconds' where ${where(id)}`);
}
async function waitRun(id: string) {
  await expect.poll(() => dbScalar(`select count(*) from escalation_runs where ${where(id)}`)).toBe("1");
}
async function confirmed(page: Page, name: string): Promise<Prepared> {
  await signIn(page, jordan);
  const alert = await prepareConfirmableAlert(page.request, `SIM-PAT-PHASE9-${name}`);
  const review = await apiJson(page.request, "get", `/api/v1/alerts/${alert.alertId}/review`);
  expect(review.escalationPlan.steps).toHaveLength(1);
  await command(page.request, `/api/v1/alerts/${alert.alertId}/confirm`, { expectedVersion: alert.draftVersion, expectedEscalationPlanRevision: review.escalationPlan.revision });
  await expect.poll(() => dbScalar(`select count(*) from delivery_attempts where ${where(alert.alertId)} and status = 'Delivered'`), { timeout: 60_000 }).toBe("1");
  await waitRun(alert.alertId);
  // The existing simulated provider persists deterministic delivered events up to one second ahead.
  // Wait for real database time to reach that evidence before issuing a positive human control.
  await expect.poll(() => dbScalar(`select clock_timestamp() >= max(greatest(requested_at_utc, submitted_at_utc, delivered_at_utc)) + interval '250 milliseconds' from delivery_attempts where ${where(alert.alertId)}`)).toBe("t");
  return alert;
}
async function deliveredOnce(id: string) {
  await expect.poll(() => dbScalar(`select count(*) from delivery_attempts d join alert_recipient_selections s on s.id=d.recipient_selection_id where d.alert_id='${id}' and s.selection_source='EscalationPolicy' and d.status='Delivered'`), { timeout: 60_000 }).toBe("1");
  expect(backups(id)).toBe("1"); expect(stepOutboxes(id)).toBe("1");
  for (const event of ["StepDue", "RecipientActivated", "DispatchQueued", "Exhausted"]) {
    expect(dbScalar(`select count(*) from escalation_events where ${where(id)} and event_type='${event}'`)).toBe("1");
  }
  expect(dbScalar(`select count(*) from delivery_attempts where ${where(id)}`)).toBe("2");
}
async function noBackup(id: string) {
  expect(backups(id)).toBe("0"); expect(stepOutboxes(id)).toBe("0");
  expect(dbScalar(`select count(*) from delivery_attempts where ${where(id)}`)).toBe("1");
}

test.describe.serial("Phase 9 real escalation", () => {
  test.beforeEach(async () => { await workers("one"); });
  test.afterEach(async () => { await workers("one"); });

  test("D: exact browser approval, database deadline, backup delivery and timeline", async ({ page }) => {
    await signIn(page, jordan);
    const alert = await prepareConfirmableAlert(page.request, "SIM-PAT-PHASE9-D");
    await page.goto(`/alerts/${alert.alertId}/review`);
    await expect(page.getByRole("heading", { name: "Exact DEMO escalation plan" })).toBeVisible();
    await expect(page.getByText(/Jules Martin/)).toBeVisible();
    await capture(page, "phase9-D-exact-plan.png");
    await page.getByRole("checkbox").check();
    await page.getByRole("button", { name: "Confirm & Dispatch" }).click();
    await expect(page.getByRole("heading", { name: "DispatchQueued" })).toBeVisible();
    await waitRun(alert.alertId);
    await workers("stop");
    due(alert.alertId);
    await workers("one");
    await deliveredOnce(alert.alertId);
    await page.goto(`/alerts/${alert.alertId}/live`);
    await expect(page.getByText(/Added by confirmed DEMO escalation policy DEMO-1, step 1/)).toBeVisible();
    await expect(page.getByText(/Step 1 · Recipient Activated/)).toBeVisible();
    await expect(page.getByText("Status: Delivered")).toHaveCount(2);
    await capture(page, "phase9-D-delivered-timeline.png");
  });

  test("E: acknowledgement does not stop escalation", async ({ page }) => {
    const alert = await confirmed(page, "E");
    await switchIdentity(page, riley);
    await command(page.request, `/api/v1/my-alerts/${alert.alertId}/responses`, { expectedVersion: alert.draftVersion, responseType: "Acknowledged" });
    await workers("stop"); due(alert.alertId); await workers("one");
    await deliveredOnce(alert.alertId);
    expect(dbScalar(`select count(*) from responsibility_assignments where ${where(alert.alertId)}`)).toBe("0");
  });

  test("F: accepted responsibility survives worker restart and prevents backup", async ({ page }) => {
    const alert = await confirmed(page, "F");
    await workers("stop");
    await switchIdentity(page, riley);
    await command(page.request, `/api/v1/my-alerts/${alert.alertId}/responses`, { expectedVersion: alert.draftVersion, responseType: "Accepted" });
    due(alert.alertId); await workers("one");
    await expect.poll(() => dbScalar(`select state || ':' || outcome from escalation_runs where ${where(alert.alertId)}`)).toBe("Stopped:ResponsibilityAccepted");
    await workers("one"); await delay(1500); await noBackup(alert.alertId);
  });

  for (const type of ["Declined", "Unavailable"]) {
    test(`G: ${type} activates early and is consumed once across restart`, async ({ page }) => {
      const alert = await confirmed(page, `G-${type}`);
      await workers("stop"); due(alert.alertId, 300);
      await switchIdentity(page, riley);
      await command(page.request, `/api/v1/my-alerts/${alert.alertId}/responses`, { expectedVersion: alert.draftVersion, responseType: type });
      await workers("one"); await deliveredOnce(alert.alertId);
      expect(dbScalar(`select count(*) from escalation_consumed_signals where ${where(alert.alertId)}`)).toBe("1");
      await workers("one"); await delay(1500); await deliveredOnce(alert.alertId);
      expect(dbScalar(`select count(*) from escalation_consumed_signals where ${where(alert.alertId)}`)).toBe("1");
    });
  }

  test("H: pause holds across restart and original deadline; resume preserves exact delay", async ({ page }) => {
    const alert = await confirmed(page, "H");
    await workers("stop"); due(alert.alertId, 10);
    const original = dbScalar(`select next_due_at_utc from escalation_runs where ${where(alert.alertId)}`);
    await page.goto(`/alerts/${alert.alertId}/live`);
    await page.getByLabel("Pause reason").selectOption("ManualCoordination");
    await page.getByRole("button", { name: "Pause escalation" }).click();
    await expect(page.getByRole("button", { name: "Resume escalation" })).toBeVisible();
    const remaining = dbScalar(`select extract(epoch from remaining_delay) from escalation_runs where ${where(alert.alertId)}`);
    expect(Number(remaining)).toBeGreaterThan(0);
    await workers("one");
    await expect.poll(() => dbScalar(`select clock_timestamp() > '${original}'::timestamptz`), { timeout: 15_000 }).toBe("t");
    await workers("one"); await delay(1500); await noBackup(alert.alertId);
    expect(dbScalar(`select state from escalation_runs where ${where(alert.alertId)}`)).toBe("Paused");
    await capture(page, "phase9-H-paused-after-restart.png");
    await page.getByRole("button", { name: "Resume escalation" }).click();
    await expect(page.getByRole("button", { name: "Pause escalation" })).toBeVisible();
    expect(dbScalar(`select abs(extract(epoch from (r.next_due_at_utc-e.occurred_at_utc)) - ${remaining}) < 0.000002 from escalation_runs r join escalation_events e on e.run_id=r.id and e.event_type='Resumed' where r.alert_id='${alert.alertId}'`)).toBe("t");
    await deliveredOnce(alert.alertId);
    for (const type of ["Paused", "Resumed"]) expect(dbScalar(`select count(*) from escalation_events where ${where(alert.alertId)} and event_type='${type}'`)).toBe("1");
  });

  test("I: two independent workers with both handlers execute one logical step", async ({ page }) => {
    const alert = await confirmed(page, "I");
    await workers("stop"); due(alert.alertId, 4);
    const pair = await workers("two");
    expect(new Set(pair.pids).size).toBe(2);
    await deliveredOnce(alert.alertId);
    await delay(1500);
    for (const pid of pair.pids) process.kill(pid, 0);
    await deliveredOnce(alert.alertId);
    console.log(`PHASE9_TWO_WORKERS pids=${pair.pids.join(",")} dispatch=true escalation=true cardinality=1`);
  });

  test("inactive confirmed backup fails visibly without selecting a replacement", async ({ page }) => {
    const alert = await confirmed(page, "inactive");
    await workers("stop");
    const backup = dbScalar(`select practitioner_id from alert_escalation_recipient_snapshots where ${where(alert.alertId)}`);
    dbScalar(`update practitioners set is_active=false where id='${backup}'`);
    try {
      due(alert.alertId); await workers("one");
      await expect.poll(() => dbScalar(`select count(*) from escalation_events where ${where(alert.alertId)} and event_type='ProcessingFailed'`)).toBe("1");
      await noBackup(alert.alertId);
      await page.goto(`/alerts/${alert.alertId}/live`);
      await expect(page.getByRole("heading", { name: "Manual fallback required" })).toBeVisible();
      await expect(page.getByText(/Escalation failure:/)).toBeVisible();
      await capture(page, "phase9-inactive-fallback.png");
    } finally { await workers("stop"); dbScalar(`update practitioners set is_active=true where id='${backup}'`); }
  });

  test("changed review rejects confirmation atomically; legacy alerts remain ineligible", async ({ page }) => {
    await signIn(page, jordan);
    const alert = await prepareConfirmableAlert(page.request, "SIM-PAT-PHASE9-changed");
    const review = await apiJson(page.request, "get", `/api/v1/alerts/${alert.alertId}/review`);
    const backup = dbScalar("select id from practitioners where simulation_code='SIM-PRAC-0103'");
    dbScalar(`update practitioners set is_active=false where id='${backup}'`);
    try {
      const response = await page.request.post(`/api/v1/alerts/${alert.alertId}/confirm`, { headers: { "Idempotency-Key": crypto.randomUUID() }, data: { expectedVersion: alert.draftVersion, expectedEscalationPlanRevision: review.escalationPlan.revision } });
      expect(response.status()).toBe(409);
      expect(dbScalar(`select count(*) from alert_escalation_plans where ${where(alert.alertId)}`)).toBe("0");
      expect(dbScalar(`select count(*) from outbox_messages where aggregate_id='${alert.alertId}'`)).toBe("0");
    } finally { dbScalar(`update practitioners set is_active=true where id='${backup}'`); }
    // Test-only historical shape: no exact approval or snapshot is fabricated.
    const legacy = await createDraft(page.request, "SIM-PAT-PHASE9-legacy");
    dbScalar(`update alerts set state='Active', confirmed_draft_version=draft_version, confirmed_at_utc=clock_timestamp()-interval '1 hour' where id='${legacy.alertId}'`);
    await workers("one"); await delay(1500);
    await page.goto(`/alerts/${legacy.alertId}/live`);
    await expect(page.getByText(/Automatic escalation is unavailable/)).toBeVisible();
    expect(dbScalar(`select count(*) from escalation_runs where ${where(legacy.alertId)}`)).toBe("0");
    expect(backups(legacy.alertId)).toBe("0"); expect(stepOutboxes(legacy.alertId)).toBe("0");
  });
});
