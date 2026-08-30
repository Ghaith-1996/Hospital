# Frontend Prototype Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current backend-connected frontend with a faithful, responsive, fully interactive local prototype of the approved nine-state operator and doctor alert workflow.

**Architecture:** Keep Next.js 16 App Router and React 19, but route every page through a shared role-aware shell and a typed alerts feature store. A reducer-backed provider persists versioned fictional state to `localStorage`; pages consume focused selectors and commands so a later backend adapter can replace the local implementation without changing the visual composition.

**Tech Stack:** Next.js 16.3.1 App Router, React 19.2.1, TypeScript 5.9.2, CSS, Vitest 3.2.7, Testing Library 16.3.0, Playwright 1.55.1.

**Spec:** `docs/superpowers/specs/2026-08-30-frontend-prototype-redesign-design.md`

**Visual source:** `docs/design/frontend-prototype-nine-screen-mockup.png`

## Global Constraints

- Frontend-only: do not modify `src/backend/`, migrations, APIs, database behavior, workers, provider adapters, or infrastructure.
- All users, clinicians, patient references, locations, timestamps, contact details, and clinical examples must be visibly fictional and synthetic.
- No page may call `/api/*`, present a fake network endpoint, or imply a real alert was sent.
- Preserve sent, delivered, opened, acknowledged, and responsibility accepted as separate concepts.
- Escalation is a fixed, clearly demo-labelled visual state; do not add timers, schedulers, retries, or automatic transitions.
- Use the existing dependencies only; do not add a UI kit, state library, icon package, web font, or runtime service.
- Treat `docs/design/frontend-prototype-nine-screen-mockup.png` as the accepted visual source of truth.
- Keep one obvious primary action per screen, keyboard operation, visible focus, semantic labels, textual status meaning, and practical 44px targets.
- Use Next.js 16 dynamic-route APIs correctly: interactive pages use `useParams()` in focused Client Components; server redirects use `redirect()` from `next/navigation`.
- Every task follows red-green-refactor, runs its focused tests, and commits only its own coherent change.

---

## Planned File Structure

```text
src/web/
|-- app/
|   |-- alerts/
|   |   |-- page.tsx
|   |   |-- new/page.tsx
|   |   `-- [id]/
|   |       |-- page.tsx
|   |       |-- review/page.tsx
|   |       |-- sent/page.tsx
|   |       |-- compose/page.tsx        # redirect only
|   |       `-- recipients/page.tsx     # redirect only
|   |-- directory/
|   |   |-- page.tsx                    # Coming later
|   |   `-- import/page.tsx             # Coming later
|   |-- my-alerts/
|   |   |-- page.tsx
|   |   `-- [id]/
|   |       |-- page.tsx
|   |       `-- respond/page.tsx
|   |-- globals.css
|   |-- layout.tsx
|   |-- page.tsx
|   `-- providers.tsx
|-- components/
|   |-- alerts/
|   |   |-- activity-timeline.tsx
|   |   |-- alert-list.tsx
|   |   |-- alert-summary.tsx
|   |   |-- clinician-selector.tsx
|   |   |-- escalation-timeline.tsx
|   |   |-- progress-steps.tsx
|   |   |-- response-panel.tsx
|   |   `-- response-summary.tsx
|   |-- layout/
|   |   |-- app-shell.tsx
|   |   `-- user-switcher.tsx
|   `-- ui/
|       |-- confirm-dialog.tsx
|       |-- icons.tsx
|       |-- page-header.tsx
|       |-- screen-state.tsx
|       |-- status-badge.tsx
|       `-- tabs.tsx
|-- features/alerts/
|   |-- prototype-store.tsx
|   |-- seed.ts
|   |-- selectors.ts
|   `-- types.ts
`-- tests/
    |-- alert-details.test.tsx
    |-- alerts-overview.test.tsx
    |-- app-shell.test.tsx
    |-- doctor-alert.test.tsx
    |-- doctor-inbox.test.tsx
    |-- new-alert.test.tsx
    |-- prototype-store.test.tsx
    |-- respond-to-alert.test.tsx
    |-- review-sent.test.tsx
    `-- test-utils.tsx
```

Files removed after their replacements are green:

- `src/web/app/development-auth-panel.tsx`
- `src/web/app/simulation-chrome.tsx`
- `src/web/app/alerts/alert-form.tsx`
- `src/web/tests/page.test.tsx`
- `src/web/tests/alert-compose.test.tsx`
- `src/web/tests/alert-recipients.test.tsx`
- `src/web/tests/alert-review.test.tsx`

`src/web/lib/alerts.ts` remains untouched but must have no imports from active prototype routes. It is retained as historical backend-client code for the later adapter phase.

---

### Task 1: Record the approved frontend-only phase boundary

**Files:**
- Modify: `AGENTS.md`
- Modify: `README.md`
- Modify: `docs/product/workflow.md`
- Modify: `docs/product/definition-of-done.md`

**Interfaces:**
- Consumes: the direct project-owner approval recorded in the design spec.
- Produces: repository instructions that authorize only the local frontend prototype and keep all backend Phase 7 boundaries intact.

- [ ] **Step 1: Prove the current phase text does not yet authorize the prototype**

Run:

```powershell
rg -n "frontend prototype|nine visual states|localStorage" AGENTS.md README.md docs/product/workflow.md docs/product/definition-of-done.md
```

Expected: no section records all three approved prototype concepts.

- [ ] **Step 2: Update the repository phase boundary**

Replace the top `Current phase` section in `AGENTS.md` with language that says:

```markdown
The active work is the approved frontend-only prototype redesign described in
`docs/superpowers/specs/2026-08-30-frontend-prototype-redesign-design.md`.
It may implement the nine fictional operator/doctor UI states with local mock
state only. Phase 7 remains the backend baseline. Do not add backend doctor
responses, live delivery behavior, escalation processing, real providers,
production identity, hospital integration, or real data.
```

Add the same boundary to `README.md`, add a `Frontend prototype surface` subsection to `docs/product/workflow.md`, and add a checklist section to `docs/product/definition-of-done.md` matching the completion criteria from the spec. Label doctor response and escalation values `SIMULATION_ONLY_ASSUMPTION` and leave all real policy values as `REQUIRES_HOSPITAL_DECISION`.

- [ ] **Step 3: Verify the documentation boundary**

Run:

```powershell
rg -n "frontend-only prototype|SIMULATION_ONLY_ASSUMPTION|REQUIRES_HOSPITAL_DECISION|No backend" AGENTS.md README.md docs/product/workflow.md docs/product/definition-of-done.md
git diff --check
```

Expected: each document states the frontend-only boundary; `git diff --check` prints nothing.

- [ ] **Step 4: Commit the phase boundary**

```powershell
git add AGENTS.md README.md docs/product/workflow.md docs/product/definition-of-done.md
git commit -m "docs: authorize frontend prototype phase"
```

---

### Task 2: Build the typed fictional alert store

**Files:**
- Create: `src/web/features/alerts/types.ts`
- Create: `src/web/features/alerts/seed.ts`
- Create: `src/web/features/alerts/selectors.ts`
- Create: `src/web/features/alerts/prototype-store.tsx`
- Create: `src/web/tests/prototype-store.test.tsx`
- Create: `src/web/tests/test-utils.tsx`
- Modify: `src/web/tests/setup.ts`

**Interfaces:**
- Consumes: React context, reducer, `localStorage`, and no network client.
- Produces: `PrototypeProvider`, `usePrototype()`, `createSeedState()`, `prototypeReducer()`, `selectAlerts()`, `selectAlertById()`, and the domain types below.

- [ ] **Step 1: Write failing tests for canonical state transitions and persistence**

Create `src/web/tests/prototype-store.test.tsx` with tests that assert:

```tsx
const draftInput: NewAlertInput = {
  patientReference: "SIM-PAT-9001",
  location: "North Wing / Simulation Room 12",
  department: "Fictional Emergency",
  urgency: "critical",
  caseDetails: "SIMULATION: fictional chest pain and hypotension.",
  clinicianIds: ["clinician-marc"],
};

it("creates one canonical draft and confirms it without implying delivery", () => {
  const initial = createSeedState();
  const created = prototypeReducer(initial, { type: "alert-created", alert: buildAlert(draftInput, "alert-new") });
  const confirmed = prototypeReducer(created, { type: "alert-confirmed", alertId: "alert-new", occurredAt: DEMO_NOW });
  const alert = selectAlertById(confirmed, "alert-new");

  expect(alert?.status).toBe("sent");
  expect(alert?.deliveryState).toBe("not-observed");
  expect(alert?.recipients[0].response).toBe("none");
});

it("records acknowledgement separately from responsibility acceptance", () => {
  const state = prototypeReducer(createSeedState(), {
    type: "doctor-responded",
    alertId: "alert-critical-1",
    clinicianId: "clinician-marc",
    response: "acknowledged",
    note: "SIMULATION: received.",
    occurredAt: DEMO_NOW,
  });

  const recipient = selectAlertById(state, "alert-critical-1")?.recipients[0];
  expect(recipient?.response).toBe("acknowledged");
  expect(recipient?.responsibilityAcceptedAt).toBeUndefined();
});

it("rejects incompatible stored state and restores deterministic seed data", () => {
  localStorage.setItem(STORAGE_KEY, JSON.stringify({ schemaVersion: 999 }));
  expect(loadPrototypeState()).toEqual(createSeedState());
});
```

Also test selected-user persistence, `reset-demo`, `declined`/`unavailable` responses, the 500-character note guard, and a storage-write failure that preserves the in-memory state while exposing `storageError`.

- [ ] **Step 2: Run the focused test and verify it fails**

Run:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Expected: FAIL because the feature modules do not exist.

- [ ] **Step 3: Define the complete domain contract**

Create `types.ts` with these exact unions and primary records:

```ts
export type UserRole = "operator" | "doctor";
export type Urgency = "routine" | "high" | "critical";
export type AlertStatus = "draft" | "sent" | "in-progress" | "resolved" | "cancelled" | "escalating";
export type DeliveryState = "not-observed" | "submitted" | "delivered" | "failed" | "not-applicable";
export type DoctorResponse = "none" | "acknowledged" | "accepted" | "declined" | "unavailable";

export type PrototypeUser = {
  id: string;
  displayName: string;
  role: UserRole;
  title: string;
  initials: string;
  clinicianId?: string;
};

export type Clinician = {
  id: string;
  displayName: string;
  initials: string;
  specialty: string;
  department: string;
  site: string;
};

export type AlertRecipient = {
  clinicianId: string;
  response: DoctorResponse;
  acknowledgedAt?: string;
  responsibilityAcceptedAt?: string;
  respondedAt?: string;
  note?: string;
};

export type AlertActivity = {
  id: string;
  kind: "created" | "sent" | "acknowledged" | "accepted" | "declined" | "unavailable" | "escalated";
  label: string;
  occurredAt: string;
  tone: "neutral" | "info" | "success" | "warning" | "critical";
};

export type EscalationStep = {
  id: string;
  label: string;
  detail: string;
  atLabel: string;
  state: "complete" | "active" | "pending";
};

export type AlertRecord = {
  id: string;
  label: string;
  patientReference: string;
  location: string;
  department: string;
  urgency: Urgency;
  caseDetails: string;
  status: AlertStatus;
  deliveryState: DeliveryState;
  createdByUserId: string;
  createdAt: string;
  updatedAt: string;
  receivedAt?: string;
  recipients: AlertRecipient[];
  activities: AlertActivity[];
  escalationSteps?: EscalationStep[];
};

export type PrototypeState = {
  schemaVersion: 1;
  selectedUserId: string;
  users: PrototypeUser[];
  clinicians: Clinician[];
  alerts: AlertRecord[];
};

export type AlertFilters = {
  status?: AlertStatus;
  urgency?: Urgency;
  department?: string;
  updatedAfter?: string;
};

export type DoctorInboxTab = "all" | "unread" | "in-progress" | "completed";

export type NewAlertInput = Pick<AlertRecord, "patientReference" | "location" | "department" | "urgency" | "caseDetails"> & {
  clinicianIds: string[];
};
```

Export `PROTOTYPE_SCHEMA_VERSION = 1`, `STORAGE_KEY = "critical-alerts.prototype.v1"`, and `DEMO_NOW = "2026-08-30T14:24:00.000Z"` from `types.ts` or `seed.ts` so tests never depend on the real clock.

- [ ] **Step 4: Implement deterministic seed, reducer, selectors, and provider**

`seed.ts` must seed Sophie Bernard, Dr. Marc Tremblay, Dr. Julie Martin, Dr. David Nguyen, exactly five operator alerts matching the mockup's status variety, three of those alerts assigned to Dr. Marc for his inbox, and one three-step escalating alert among the five. Every string is prefixed or contextually labelled as fictional/simulation data.

`selectors.ts` exports:

```ts
export function selectCurrentUser(state: PrototypeState): PrototypeUser;
export function selectAlertById(state: PrototypeState, id: string): AlertRecord | undefined;
export function selectAlerts(state: PrototypeState, filters: AlertFilters): AlertRecord[];
export function selectDoctorAlerts(state: PrototypeState, clinicianId: string, tab: DoctorInboxTab): AlertRecord[];
export function searchClinicians(state: PrototypeState, query: string): Clinician[];
```

`prototype-store.tsx` exports these exact helpers before the context contract:

```ts
export function buildAlert(input: NewAlertInput, id: string): AlertRecord;
export function loadPrototypeState(storage?: Pick<Storage, "getItem">): PrototypeState;
export function savePrototypeState(state: PrototypeState, storage?: Pick<Storage, "setItem">): void;
export function prototypeReducer(state: PrototypeState, action: PrototypeAction): PrototypeState;
```

`buildAlert` derives `label` from the first sentence of `caseDetails`, trims it to 64 characters, uses `DEMO_NOW` for deterministic created/updated activity, sets `deliveryState: "not-observed"`, and creates one `response: "none"` recipient per selected clinician. `PrototypeAction` is a discriminated union for `user-selected`, `alert-created`, `alert-updated`, `alert-confirmed`, `doctor-responded`, and `demo-reset` with the payloads exercised in the tests.

The file also exports `PrototypeProvider` and:

```ts
export type PrototypeContextValue = {
  state: PrototypeState;
  hydrated: boolean;
  storageError: string | null;
  selectUser(userId: string): void;
  createAlert(input: NewAlertInput): string;
  updateAlert(alertId: string, input: NewAlertInput): void;
  confirmAlert(alertId: string): void;
  respondToAlert(alertId: string, clinicianId: string, response: Exclude<DoctorResponse, "none">, note: string): void;
  resetDemo(): void;
};

export function usePrototype(): PrototypeContextValue;
```

`PrototypeProvider` accepts optional `initialState?: PrototypeState` and `storage?: Pick<Storage, "getItem" | "setItem" | "removeItem">` props for focused tests. When `initialState` is supplied, use it directly and skip browser-storage hydration. Otherwise initialize the same deterministic seed on server and first client render, load stored state in `useEffect`, and persist reducer changes after hydration. Catch storage exceptions, keep current memory state, and set the accessible message `Demo changes are available for this session but could not be saved in this browser.`

Create `tests/test-utils.tsx` with:

```tsx
export function renderPrototype(
  ui: React.ReactElement,
  options: { state?: PrototypeState } = {},
) {
  return render(
    <PrototypeProvider initialState={options.state ?? createSeedState()}>
      {ui}
    </PrototypeProvider>,
  );
}
```

Update `tests/setup.ts` so `afterEach` runs both `cleanup()` and `localStorage.clear()`.

- [ ] **Step 5: Run the focused test and verify it passes**

Run:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Expected: PASS with no fetch calls.

- [ ] **Step 6: Commit the store**

```powershell
git add src/web/features/alerts src/web/tests/prototype-store.test.tsx src/web/tests/test-utils.tsx src/web/tests/setup.ts
git commit -m "feat(web): add fictional alert prototype store"
```

---

### Task 3: Establish the visual system and role-aware app shell

**Files:**
- Create: `src/web/components/ui/icons.tsx`
- Create: `src/web/components/ui/status-badge.tsx`
- Create: `src/web/components/ui/tabs.tsx`
- Create: `src/web/components/ui/page-header.tsx`
- Create: `src/web/components/ui/screen-state.tsx`
- Create: `src/web/components/layout/app-shell.tsx`
- Create: `src/web/components/layout/user-switcher.tsx`
- Create: `src/web/app/providers.tsx`
- Create: `src/web/tests/app-shell.test.tsx`
- Modify: `src/web/app/layout.tsx`
- Modify: `src/web/app/page.tsx`
- Replace: `src/web/app/globals.css`
- Delete: `src/web/app/development-auth-panel.tsx`
- Delete: `src/web/app/simulation-chrome.tsx`
- Delete: `src/web/tests/page.test.tsx`

**Interfaces:**
- Consumes: `PrototypeProvider`, `usePrototype()`, `PrototypeUser`, `AlertStatus`, and `Urgency` from Task 2.
- Produces: `AppShell`, `UserSwitcher`, `StatusBadge`, `Tabs`, `PageHeader`, `ScreenState`, and the global CSS class contract used by every page.

- [ ] **Step 1: Write failing shell tests**

Create `app-shell.test.tsx` and render pages inside `PrototypeProvider`. Assert:

```tsx
expect(screen.getByRole("status", { name: "SIMULATION" })).toBeVisible();
expect(screen.getByRole("navigation", { name: "Operator navigation" })).toBeVisible();
expect(screen.getByRole("link", { name: "Alert Doctor" })).toHaveAttribute("href", "/alerts/new");
expect(screen.getByRole("link", { name: "Alerts" })).toHaveAttribute("href", "/alerts");
expect(screen.getByRole("button", { name: "Directory — Coming later" })).toBeDisabled();

fireEvent.click(screen.getByRole("button", { name: /Sophie Bernard/ }));
fireEvent.click(screen.getByRole("menuitem", { name: /Dr. Marc Tremblay/ }));
expect(mockReplace).toHaveBeenCalledWith("/my-alerts");
expect(screen.getByRole("navigation", { name: "Doctor navigation" })).toBeVisible();
```

Also test the reset action, mobile navigation toggle `aria-expanded`, active-link `aria-current="page"`, and the root loading state before hydration.

- [ ] **Step 2: Run the shell test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/app-shell.test.tsx
```

Expected: FAIL because the shell components do not exist.

- [ ] **Step 3: Implement shared visual primitives**

`icons.tsx` exports named 20px SVG components for Shield, Bell, List, Inbox, Directory, Report, Settings, Chevron, User, Clock, Check, Alert, Filter, Search, Close, Menu, and More. Each accepts `className?: string`, uses `viewBox="0 0 24 24"`, `aria-hidden="true"`, `focusable="false"`, `fill="none"`, `stroke="currentColor"`, `strokeWidth={1.8}`, and rounded caps/joins.

`StatusBadge` maps urgency/status to both visible text and tone:

```tsx
<span className={`status-badge status-badge--${tone}`}>
  <span className="status-badge__dot" aria-hidden="true" />
  {label}
</span>
```

`Tabs` uses buttons with `role="tab"`, `aria-selected`, roving keyboard focus for Left/Right, and an optional count badge. `ScreenState` renders loading, empty, not-found, or recoverable-storage messages with an optional action. `PageHeader` accepts `title`, `description`, and optional `actions`, renders the page's unique `<h1>`, and uses the shared `.page-header` layout on every primary route.

- [ ] **Step 4: Implement the provider, shell, user menu, and root role redirect**

`providers.tsx` is a Client Component that composes:

```tsx
export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <PrototypeProvider>
      <AppShell>{children}</AppShell>
    </PrototypeProvider>
  );
}
```

`layout.tsx` keeps `<html lang="en">`, updates metadata to `Critical Alerts - Simulation Prototype`, and wraps children in `<Providers>`. `AppShell` uses `usePathname()` to select the active link. Operator navigation contains Alert Doctor and Alerts; doctor navigation contains Inbox. Directory, Reports, and Settings are semantic disabled buttons with `title="Coming later"`. The user menu exposes both fictional users and Reset demo data, closes on Escape/outside click, and redirects with `router.replace()` after a role change.

`page.tsx` is a focused Client Component: wait until `hydrated`, then `router.replace(currentUser.role === "doctor" ? "/my-alerts" : "/alerts/new")`. Render `<ScreenState kind="loading" label="Loading fictional demo workspace" />` while waiting. `AppShell` also withholds role-specific navigation until hydration completes so a stored doctor never sees an operator-navigation flash.

- [ ] **Step 5: Replace global CSS with the accepted design tokens and shell layout**

Start `globals.css` with these exact tokens:

```css
:root {
  color-scheme: light;
  --background: #ffffff;
  --surface: #ffffff;
  --surface-muted: #f8fafc;
  --border: #dfe5ee;
  --border-strong: #c9d2df;
  --text: #111827;
  --text-secondary: #4b5563;
  --text-muted: #6b7280;
  --primary: #0b63f6;
  --primary-hover: #0754d4;
  --primary-soft: #eaf2ff;
  --critical: #dc2626;
  --critical-soft: #fef2f2;
  --warning: #d97706;
  --warning-soft: #fffbeb;
  --success: #16a34a;
  --success-soft: #ecfdf3;
  --focus: #2563eb;
  --sidebar-width: 232px;
  --content-max: 1180px;
  --radius-sm: 6px;
  --radius-md: 9px;
  --shadow-soft: 0 1px 2px rgb(15 23 42 / 5%);
}
```

Use `font-family: "Segoe UI", Arial, sans-serif`, a 14px base, true-white surfaces, the fixed desktop sidebar, bounded content, explicit button/control typography, 44px controls, visible `:focus-visible`, and `prefers-reduced-motion`. Add tablet drawer behavior below 960px and mobile top-bar behavior below 640px; do not use a bottom nav because it is absent from the accepted concept.

- [ ] **Step 6: Remove obsolete shell code and run the focused checks**

Remove the three obsolete files only after imports point to the new shell. Run:

```powershell
npm --prefix src/web test -- --run tests/app-shell.test.tsx tests/prototype-store.test.tsx
npm --prefix src/web run typecheck
```

Expected: both test files PASS and typecheck exits 0.

- [ ] **Step 7: Commit the shell**

```powershell
git add src/web/app src/web/components src/web/tests/app-shell.test.tsx src/web/tests/page.test.tsx
git commit -m "feat(web): add prototype design system and shell"
```

---

### Task 4: Implement New Alert and clinician selection

**Files:**
- Create: `src/web/components/alerts/alert-summary.tsx`
- Create: `src/web/components/alerts/clinician-selector.tsx`
- Create: `src/web/tests/new-alert.test.tsx`
- Replace: `src/web/app/alerts/new/page.tsx`
- Delete: `src/web/app/alerts/alert-form.tsx`
- Delete: `src/web/tests/alert-compose.test.tsx`
- Delete: `src/web/tests/alert-recipients.test.tsx`

**Interfaces:**
- Consumes: `usePrototype().state`, `usePrototype().createAlert()`, `searchClinicians()`, `Clinician`, `NewAlertInput`, and shared UI/icon classes.
- Produces: `AlertSummary`, `ClinicianSelector`, and a complete `/alerts/new` local workflow that navigates to `/alerts/{id}/review`.

- [ ] **Step 1: Write failing tests for form behavior**

Create `new-alert.test.tsx` with a router mock and these assertions:

```tsx
it("shows validation only after submit and requires one clinician", async () => {
  renderPrototype(<NewAlertPage />);
  expect(screen.queryByText("Patient reference is required.")).not.toBeInTheDocument();

  fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "" } });
  fireEvent.change(screen.getByLabelText("Case Details"), { target: { value: "" } });
  fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

  expect(screen.getByText("Patient reference is required.")).toBeVisible();
  expect(screen.getByText("Case details are required.")).toBeVisible();
  expect(screen.getByText("Select at least one fictional clinician.")).toBeVisible();
  expect(mockPush).not.toHaveBeenCalled();
});

it("searches, selects, removes, and creates one local draft", () => {
  renderPrototype(<NewAlertPage />);
  fireEvent.change(screen.getByLabelText("Search fictional clinicians"), { target: { value: "cardiology" } });
  fireEvent.click(screen.getByRole("button", { name: "Add Dr. Marc Tremblay" }));
  expect(screen.getByText("Selected Clinicians (1)")).toBeVisible();
  expect(screen.getByTestId("alert-summary")).toHaveTextContent("Dr. Marc Tremblay");
  fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));
  expect(mockPush).toHaveBeenCalledWith(expect.stringMatching(/^\/alerts\/alert-[a-z0-9-]+\/review$/));
});
```

Also test Type/Dictate toggling, the disabled dictation message, 4000-character counter, Remove, Clear, and preserved input after a validation failure. Add a test that loads `/alerts/new?edit=alert-critical-1`, pre-fills the canonical alert, updates the case details through `updateAlert`, and returns to the same review ID.

- [ ] **Step 2: Run the focused test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/new-alert.test.tsx
```

Expected: FAIL against the old backend-connected page.

- [ ] **Step 3: Implement `ClinicianSelector` and `AlertSummary`**

Use these props:

```ts
export type ClinicianSelectorProps = {
  clinicians: Clinician[];
  selectedIds: string[];
  query: string;
  onQueryChange(query: string): void;
  onAdd(id: string): void;
  onRemove(id: string): void;
  error?: string;
};

export type AlertSummaryProps = {
  patientReference: string;
  urgency: Urgency;
  caseDetails: string;
  selectedClinicians: Clinician[];
};
```

Search filters name, specialty, and department case-insensitively. Results use an explicit Add button; selected rows use an explicit Remove button. Do not preselect or rank clinicians. The summary shows `Not specified`/`None selected` when values are empty and uses `data-testid="alert-summary"` for focused tests.

- [ ] **Step 4: Replace the New Alert page with the accepted composition**

Use local component state with defaults `critical`, `type`, and an empty patient reference/case description. Render:

```tsx
<PageHeader title="Alert Doctor" description="Create a new alert and notify the right clinician, fast." />
<form className="new-alert-layout" onSubmit={handleSubmit} noValidate>
  <section className="new-alert-form">...</section>
  <AlertSummary ... />
</form>
```

The left column contains New Alert, Patient Reference, Urgency Level, Case Details, Type/Dictate controls, the 4000-character textarea, the simulation information notice, and ClinicianSelector. Keep location and department out of this screen because they are absent from the accepted composition; new records use `Fictional ER - Simulation Bed 12` and `Fictional Emergency` as explicit local defaults. The bottom action row contains Clear and the single blue Review & Confirm action. After hydration, read an optional edit ID from `new URLSearchParams(window.location.search).get("edit")`; when it resolves to an alert, pre-fill the form and selected clinicians. `handleSubmit` validates all four requirements, calls `updateAlert(editId, input)` for edit mode or `createAlert(input)` for a new draft, and pushes to that alert's review route.

Dictate renders a disabled recording panel reading `Dictation is not connected in this frontend prototype. Type the fictional case details instead.` It never calls browser microphone APIs.

- [ ] **Step 5: Remove obsolete split-flow files and run checks**

```powershell
npm --prefix src/web test -- --run tests/new-alert.test.tsx tests/prototype-store.test.tsx
npm --prefix src/web run typecheck
```

Expected: PASS; active New Alert code contains no `fetch(` and no `/api/` string.

- [ ] **Step 6: Commit New Alert**

```powershell
git add src/web/app/alerts src/web/components/alerts src/web/tests
git commit -m "feat(web): build local new alert workflow"
```

---

### Task 5: Implement review, deliberate confirmation, and sent state

**Files:**
- Create: `src/web/components/alerts/progress-steps.tsx`
- Create: `src/web/components/ui/confirm-dialog.tsx`
- Create: `src/web/app/alerts/[id]/sent/page.tsx`
- Create: `src/web/tests/review-sent.test.tsx`
- Replace: `src/web/app/alerts/[id]/review/page.tsx`
- Delete: `src/web/tests/alert-review.test.tsx`

**Interfaces:**
- Consumes: `selectAlertById()`, `usePrototype().confirmAlert()`, `ProgressSteps`, and shared status/UI components.
- Produces: exact review route, accessible confirmation dialog, and sent-success route.

- [ ] **Step 1: Write failing review and sent tests**

Create `review-sent.test.tsx` and assert:

```tsx
expect(screen.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
expect(screen.getByText("Dr. Marc Tremblay")).toBeVisible();

fireEvent.click(screen.getByRole("button", { name: "Confirm & Dispatch" }));
expect(screen.getByRole("dialog", { name: "Confirm alert dispatch?" })).toBeVisible();
expect(screen.getByText(/send this fictional alert to 3 clinicians/i)).toBeVisible();
fireEvent.click(screen.getByRole("button", { name: "Confirm fictional dispatch" }));
expect(mockPush).toHaveBeenCalledWith("/alerts/alert-critical-1/sent");

renderPrototype(<AlertSentPage />);
expect(screen.getByRole("heading", { name: "Alert Sent Successfully!" })).toBeVisible();
expect(screen.getByText(/simulated sending to 3 fictional clinicians/i)).toBeVisible();
```

Also test Cancel, Escape closing, focus return, missing-alert not-found state, Create Another Alert, and View Alert Details.

- [ ] **Step 2: Run the focused test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/review-sent.test.tsx
```

Expected: FAIL because the sent route and dialog do not exist.

- [ ] **Step 3: Implement progress steps and accessible dialog**

`ProgressSteps` receives `current: 1 | 2 | 3` and always renders New Alert, Review & Confirm, Alert Sent. Completed/current states have textual screen-reader labels.

`ConfirmDialog` uses conditional rendering with `role="dialog"`, `aria-modal="true"`, `aria-labelledby`, initial focus on Cancel, Escape handling, Tab/Shift+Tab focus wrapping, backdrop close disabled, and focus return to the trigger. Props:

```ts
type ConfirmDialogProps = {
  open: boolean;
  recipientNames: string[];
  onCancel(): void;
  onConfirm(): void;
};
```

- [ ] **Step 4: Replace review and create sent pages**

The review page uses `useParams()` and `selectAlertById`. It renders the exact patient reference, urgency, case details, and clinician list from the canonical store; Back/Edit returns to `/alerts/new?edit={id}` so the same draft is pre-filled; Cancel returns to `/alerts`; Confirm & Dispatch only opens the dialog. Dialog confirmation calls `confirmAlert(id)` and routes to `/alerts/{id}/sent`.

The sent page renders current step 3, the restrained green check, `Alert Sent Successfully!`, the explicit simulation sentence, the What happens next panel, and two actions. It must not use confetti, timers, or animation beyond a reduced-motion-safe opacity transition.

- [ ] **Step 5: Run focused checks**

```powershell
npm --prefix src/web test -- --run tests/review-sent.test.tsx tests/new-alert.test.tsx tests/prototype-store.test.tsx
npm --prefix src/web run typecheck
```

Expected: PASS.

- [ ] **Step 6: Commit review and sent**

```powershell
git add src/web/app/alerts/[id]/review src/web/app/alerts/[id]/sent src/web/components src/web/tests
git commit -m "feat(web): add review confirmation and sent states"
```

---

### Task 6: Implement Alerts Overview, tabs, filters, table, and mobile cards

**Files:**
- Create: `src/web/components/alerts/alert-list.tsx`
- Create: `src/web/app/alerts/page.tsx`
- Create: `src/web/tests/alerts-overview.test.tsx`
- Modify: `src/web/app/globals.css`

**Interfaces:**
- Consumes: `selectAlerts()`, `AlertFilters`, `AlertRecord`, `StatusBadge`, `Tabs`, and Next `Link`.
- Produces: `AlertList` with semantic desktop table and equivalent mobile cards, plus working local tabs and filter drawer.

- [ ] **Step 1: Write failing overview tests**

Create `alerts-overview.test.tsx` and assert:

```tsx
expect(screen.getByRole("heading", { name: "Alerts" })).toBeVisible();
expect(screen.getByRole("table", { name: "Fictional alerts" })).toBeVisible();
expect(screen.getAllByRole("row")).toHaveLength(6); // header plus five seeded rows

fireEvent.click(screen.getByRole("tab", { name: "Draft" }));
expect(screen.getByText("DRAFT-0012")).toBeVisible();
expect(screen.queryByText("SIM-PAT-01578")).not.toBeInTheDocument();

fireEvent.click(screen.getByRole("button", { name: "Filters" }));
fireEvent.change(screen.getByLabelText("Urgency"), { target: { value: "critical" } });
fireEvent.click(screen.getByRole("button", { name: "Apply filters" }));
expect(screen.getAllByText("Critical").length).toBeGreaterThan(0);
```

Also assert that row links target `/alerts/{id}`, Clear filters restores all rows, empty filters render a useful empty state, and each mobile card exposes the same five fields.

- [ ] **Step 2: Run the overview test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

Expected: FAIL because `/alerts` does not exist.

- [ ] **Step 3: Implement `AlertList`**

Render one semantic table with caption `Fictional alerts` and columns Patient Reference, Urgency, Status, Recipients, Last Updated, and an unlabeled navigation column with an accessible `Open {patientReference}` link. Do not attach click handlers to `<tr>`. Render a `.alert-cards` list using the same records for mobile; CSS shows the table at 641px and above and cards at 640px and below.

Recipient display is `{responded}/{total}` where responded means any response other than `none`; status remains separate from response and delivery.

- [ ] **Step 4: Implement local tabs and filter drawer**

The page owns `statusTab`, `draftFilters`, `appliedFilters`, and `filtersOpen`. Tabs are All, Draft, Sent, In Progress, Resolved, Cancelled. The drawer contains urgency, status, date window, and department controls with Apply and Clear. `selectAlerts` performs the combined filtering. The Filters button includes an accessible active-filter count.

- [ ] **Step 5: Run focused checks and commit**

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx tests/app-shell.test.tsx
npm --prefix src/web run typecheck
npm --prefix src/web run lint
git add src/web/app/alerts/page.tsx src/web/components/alerts/alert-list.tsx src/web/app/globals.css src/web/tests/alerts-overview.test.tsx
git commit -m "feat(web): add local alerts overview"
```

Expected: tests, typecheck, and lint PASS before the commit.

---

### Task 7: Implement alert details, activity, responses, and demo escalation

**Files:**
- Create: `src/web/components/alerts/activity-timeline.tsx`
- Create: `src/web/components/alerts/response-summary.tsx`
- Create: `src/web/components/alerts/escalation-timeline.tsx`
- Create: `src/web/app/alerts/[id]/page.tsx`
- Create: `src/web/tests/alert-details.test.tsx`
- Modify: `src/web/app/globals.css`

**Interfaces:**
- Consumes: `AlertRecord`, `AlertActivity`, `EscalationStep`, `Clinician`, `selectAlertById()`, and shared status/screen-state primitives.
- Produces: reusable activity, response-summary, and escalation components plus `/alerts/[id]`.

- [ ] **Step 1: Write failing detail and escalation tests**

Create `alert-details.test.tsx` with:

```tsx
navigation.params.id = "alert-critical-1";
renderPrototype(<AlertDetailsPage />);
expect(screen.getByRole("heading", { name: "Alert Details" })).toBeVisible();
expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
expect(screen.getByRole("region", { name: "Activity Timeline" })).toBeVisible();
expect(screen.getByRole("region", { name: "Responses Summary" })).toBeVisible();
expect(screen.getByText("Acknowledged")).toBeVisible();
expect(screen.getByText("Accepted")).toBeVisible();

cleanup();
navigation.params.id = "alert-escalating-1";
renderPrototype(<AlertDetailsPage />);
expect(screen.getByRole("heading", { name: "Alert Escalation" })).toBeVisible();
expect(screen.getByText("DEMO elapsed time: 12 min")).toBeVisible();
expect(screen.getByText("Escalating to fictional on-call cardiologist")).toBeVisible();
expect(screen.queryByText(/next update in/i)).not.toBeInTheDocument();
```

Also test missing alert, explicit delivery-state text, clinician names, response counts, timeline order, and the View Policy disabled/Coming later treatment.

- [ ] **Step 2: Run the detail test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/alert-details.test.tsx
```

Expected: FAIL because the detail route and components do not exist.

- [ ] **Step 3: Implement reusable monitoring components**

`ActivityTimeline` sorts a copied activity array ascending by `occurredAt`, uses `<ol>`, and prints a visible time plus label. `ResponseSummary` counts accepted, acknowledged, declined/unavailable, and no response separately. `EscalationTimeline` renders a vertical `<ol>` with Complete, In progress, and Pending text; no `setInterval`, timeout, date subtraction, or effect changes step state.

- [ ] **Step 4: Implement both detail compositions in one route**

Use `useParams()` and return `ScreenState kind="not-found"` when absent. Normal details reproduce the mockup's top three panels and lower timeline/summary grid. Escalating details use the alert escalation header, metadata row, and escalation progress panel while preserving Back to Alerts and explicit Critical/Escalating text.

Every operational state has text: delivery state uses `Not observed`, `Submitted`, `Delivered`, `Failed`, or `Not applicable`; acknowledgement and acceptance appear only in recipient responses.

- [ ] **Step 5: Run focused checks and commit**

```powershell
npm --prefix src/web test -- --run tests/alert-details.test.tsx tests/alerts-overview.test.tsx tests/prototype-store.test.tsx
npm --prefix src/web run typecheck
git add src/web/app/alerts/[id]/page.tsx src/web/components/alerts src/web/app/globals.css src/web/tests/alert-details.test.tsx
git commit -m "feat(web): add alert monitoring and demo escalation"
```

Expected: PASS.

---

### Task 8: Implement Doctor Inbox

**Files:**
- Create: `src/web/app/my-alerts/page.tsx`
- Create: `src/web/tests/doctor-inbox.test.tsx`
- Modify: `src/web/app/globals.css`

**Interfaces:**
- Consumes: `selectCurrentUser()`, `selectDoctorAlerts()`, `AlertList` display conventions, `Tabs`, and `StatusBadge`.
- Produces: `/my-alerts` with All, Unread, In Progress, and Completed local tabs.

- [ ] **Step 1: Write failing Doctor Inbox tests**

Create `doctor-inbox.test.tsx` and select Dr. Marc in the initial test state. Assert:

```tsx
expect(screen.getByRole("heading", { name: "Inbox" })).toBeVisible();
expect(screen.getByText("Alerts assigned to me.")).toBeVisible();
expect(screen.getAllByRole("row")).toHaveLength(4);
expect(screen.getByRole("tab", { name: /Unread 1/ })).toBeVisible();

fireEvent.click(screen.getByRole("tab", { name: /Unread 1/ }));
expect(screen.getByText("Chest pain, hypotension")).toBeVisible();
expect(screen.queryByText("Suspected sepsis")).not.toBeInTheDocument();
expect(screen.getByRole("link", { name: /Open Chest pain, hypotension/ })).toHaveAttribute(
  "href",
  "/my-alerts/alert-critical-1",
);
```

Also test Completed, In Progress, empty state, and mobile card field equivalence.

- [ ] **Step 2: Run the inbox test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/doctor-inbox.test.tsx
```

Expected: FAIL because `/my-alerts` does not exist.

- [ ] **Step 3: Implement the doctor-scoped inbox**

Use the current fictional doctor's `clinicianId`; if the selected user is not a doctor, render a `ScreenState` action that switches to Dr. Marc instead of silently showing operator data. The desktop table columns are Alert, Patient Reference, Urgency, Status, and Received. Mobile cards show the same values. Unread means recipient response `none`; In Progress means acknowledged/accepted or alert status `in-progress`; Completed means resolved or a completed recipient state.

- [ ] **Step 4: Run checks and commit**

```powershell
npm --prefix src/web test -- --run tests/doctor-inbox.test.tsx tests/app-shell.test.tsx
npm --prefix src/web run typecheck
git add src/web/app/my-alerts/page.tsx src/web/app/globals.css src/web/tests/doctor-inbox.test.tsx
git commit -m "feat(web): add fictional doctor inbox"
```

Expected: PASS.

---

### Task 9: Implement Doctor Alert and focused response flow

**Files:**
- Create: `src/web/components/alerts/response-panel.tsx`
- Create: `src/web/app/my-alerts/[id]/page.tsx`
- Create: `src/web/app/my-alerts/[id]/respond/page.tsx`
- Create: `src/web/tests/doctor-alert.test.tsx`
- Create: `src/web/tests/respond-to-alert.test.tsx`
- Modify: `src/web/app/globals.css`

**Interfaces:**
- Consumes: `selectAlertById()`, selected doctor's `clinicianId`, `usePrototype().respondToAlert()`, and `DoctorResponse`.
- Produces: `ResponsePanel`, `/my-alerts/[id]`, and `/my-alerts/[id]/respond` with canonical cross-role updates.

- [ ] **Step 1: Write failing Doctor Alert tests**

Create `doctor-alert.test.tsx` and assert patient reference, location, case details, other recipients, received time, critical badge, and the sticky response region. Assert four actions:

```tsx
expect(screen.getByRole("button", { name: "Acknowledge" })).toBeVisible();
expect(screen.getByRole("button", { name: "Accept" })).toBeVisible();
expect(screen.getByRole("button", { name: "Decline" })).toBeVisible();
expect(screen.getByRole("button", { name: "Unavailable" })).toBeVisible();
fireEvent.click(screen.getByRole("button", { name: "Accept" }));
expect(mockPush).toHaveBeenCalledWith("/my-alerts/alert-critical-1/respond?response=accepted");
```

Also test not-found and a visible current-response state after submission.

- [ ] **Step 2: Write failing focused response tests**

Create `respond-to-alert.test.tsx` and assert the four radio options with their exact plain-language descriptions, default selection from `?response=accepted`, optional note, `0 / 500 characters`, Cancel, and Submit Response. Submit an acknowledgement and assert:

```tsx
await waitFor(() => {
  expect(selectAlertById(loadPrototypeState(), "alert-critical-1")?.recipients[0].response).toBe("acknowledged");
});
expect(selectAlertById(loadPrototypeState(), "alert-critical-1")?.recipients[0].responsibilityAcceptedAt).toBeUndefined();
expect(mockPush).toHaveBeenCalledWith("/my-alerts/alert-critical-1?responded=1");
```

Also test the 500-character maximum and that Accept alone sets `responsibilityAcceptedAt`.

- [ ] **Step 3: Run both tests and verify they fail**

```powershell
npm --prefix src/web test -- --run tests/doctor-alert.test.tsx tests/respond-to-alert.test.tsx
```

Expected: FAIL because both routes and ResponsePanel do not exist.

- [ ] **Step 4: Implement Doctor Alert and `ResponsePanel`**

Doctor Alert reproduces the mockup's compact three-column detail area and bottom sticky action bar. `ResponsePanel` accepts `alertId`, `currentResponse`, and `onChoose(response)`. It renders Acknowledge, Accept, Decline, Unavailable, and an inert More button with `title="Coming later"`. Choosing a response routes to the focused response page; it does not mutate state immediately.

- [ ] **Step 5: Implement Respond to Alert**

Use `useParams()` and `useSearchParams()` in this client-only local filtering flow. Render exact descriptions:

- Acknowledge - `I have received this alert.`
- Accept - `I will take responsibility for this fictional case.`
- Decline - `I am not able to take this fictional case.`
- Unavailable - `I am currently unavailable.`

Keep the note optional, clamp it to 500 characters, and display the live count. Submit calls `respondToAlert`, which updates the recipient response and adds one canonical activity event. Acknowledge never sets responsibility acceptance; Accept sets it. Then route back with `?responded=1` and show an accessible success message on the alert view.

- [ ] **Step 6: Run focused checks and commit**

```powershell
npm --prefix src/web test -- --run tests/doctor-alert.test.tsx tests/respond-to-alert.test.tsx tests/prototype-store.test.tsx
npm --prefix src/web run typecheck
npm --prefix src/web run lint
git add src/web/app/my-alerts src/web/components/alerts/response-panel.tsx src/web/app/globals.css src/web/tests
git commit -m "feat(web): add fictional doctor response flow"
```

Expected: PASS.

---

### Task 10: Redirect legacy routes and add approved Coming later states

**Files:**
- Replace: `src/web/app/alerts/[id]/compose/page.tsx`
- Replace: `src/web/app/alerts/[id]/recipients/page.tsx`
- Replace: `src/web/app/directory/page.tsx`
- Replace: `src/web/app/directory/import/page.tsx`
- Modify: `src/web/tests/app-shell.test.tsx`

**Interfaces:**
- Consumes: Next.js `redirect()` and shared `ScreenState`.
- Produces: unambiguous redirects and consistent non-functional Directory pages.

- [ ] **Step 1: Add failing route-boundary tests**

Extend `app-shell.test.tsx` to assert both Directory components render `Directory is coming later` with no search/import/fetch controls. Import both legacy route components, mock `redirect`, invoke each component, and assert two calls to `redirect("/alerts/new")`.

- [ ] **Step 2: Run the focused test and verify it fails**

```powershell
npm --prefix src/web test -- --run tests/app-shell.test.tsx
```

Expected: FAIL because the old backend-connected routes remain.

- [ ] **Step 3: Replace the four route implementations**

Each legacy alert page is a Server Component containing only:

```tsx
import { redirect } from "next/navigation";

export default function LegacyAlertRoute() {
  redirect("/alerts/new");
}
```

Directory and Directory Import render the shared ScreenState with title `Directory is coming later`, explanation `The redesigned frontend is local-only. A future backend phase will reconnect fictional directory management.`, and a link to Alert Doctor. They do not import the old directory client or call `fetch`.

- [ ] **Step 4: Verify active routes contain no network behavior and commit**

```powershell
rg -n "fetch\(|/api/" src/web/app src/web/components src/web/features
npm --prefix src/web test -- --run tests/app-shell.test.tsx
npm --prefix src/web run typecheck
git add src/web/app/alerts/[id]/compose src/web/app/alerts/[id]/recipients src/web/app/directory src/web/tests/app-shell.test.tsx
git commit -m "refactor(web): retire legacy backend-connected routes"
```

Expected: `rg` finds no active route/component/store network calls; test and typecheck PASS. A match inside retained `src/web/lib/alerts.ts` is acceptable because that directory is intentionally outside this scan.

---

### Task 11: Complete responsive, accessibility, browser, and visual verification

**Files:**
- Create: `tests/e2e/frontend-prototype.spec.ts`
- Delete: `tests/e2e/platform-smoke.spec.ts`
- Modify: `src/web/app/globals.css`
- Modify: `docs/product/definition-of-done.md`

**Interfaces:**
- Consumes: all finished routes, the accepted mockup, Playwright, Browser/IAB, and `view_image`.
- Produces: end-to-end workflow evidence, responsive behavior, accessibility fixes, and the final phase review record.

- [ ] **Step 1: Write failing end-to-end tests for the core workflows**

Create `frontend-prototype.spec.ts` with four tests:

```ts
test("operator creates, reviews, sends, and opens a fictional alert", async ({ page }) => {
  await page.goto("/alerts/new");
  await page.getByLabel("Patient Reference").fill("SIM-PAT-E2E-001");
  await page.getByLabel("Case Details").fill("SIMULATION: fictional E2E alert details.");
  await page.getByLabel("Search fictional clinicians").fill("Marc");
  await page.getByRole("button", { name: "Add Dr. Marc Tremblay" }).click();
  await page.getByRole("button", { name: "Review & Confirm" }).click();
  await expect(page.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
  await page.getByRole("button", { name: "Confirm & Dispatch" }).click();
  await page.getByRole("button", { name: "Confirm fictional dispatch" }).click();
  await expect(page.getByRole("heading", { name: "Alert Sent Successfully!" })).toBeVisible();
  await page.getByRole("link", { name: "View Alert Details" }).click();
  await expect(page.getByRole("heading", { name: "Alert Details" })).toBeVisible();
});
```

The other tests must:

1. switch to Dr. Marc, open Inbox, acknowledge an alert, and verify acknowledgement is visible without acceptance;
2. filter the operator overview and open the demo escalation alert without any automatic step change; and
3. use a 390x844 viewport, open/close mobile navigation, create an alert, verify table-to-card layout, and confirm no horizontal overflow via `document.documentElement.scrollWidth <= window.innerWidth`.

- [ ] **Step 2: Run E2E and capture the initial failures**

```powershell
npm run web:e2e
```

Expected: any remaining selector, workflow, or responsive mismatch fails with a specific route/control name.

- [ ] **Step 3: Finish responsive and accessibility CSS/behavior**

At 960px, turn the sidebar into an off-canvas drawer with a visible Menu button and focus return. At 640px, stack form/detail grids, move summaries below primary content, replace tables with cards, keep response actions visible without covering content, and ensure all controls remain at least 44px high. Add skip link, `main` target, body scroll lock while drawer/dialog is open, and no animation under `prefers-reduced-motion`.

Run keyboard-only checks for sidebar, user menu, tabs, filters, clinician add/remove, dialog, and doctor response. Verify unique page `<h1>` headings so Next.js route announcements remain meaningful.

- [ ] **Step 4: Run the complete automated frontend and safety suite**

From the repository root run:

```powershell
npm --prefix src/web test -- --run
npm --prefix src/web run typecheck
npm --prefix src/web run lint
npm --prefix src/web run build
npm run web:e2e
powershell -ExecutionPolicy Bypass -File scripts/verify-no-sensitive-data.ps1
git diff --check
```

Expected: all commands exit 0. If the repository's full `scripts/test-all.ps1` environment is available, run it as the final regression gate and record its exact totals; otherwise record why the frontend-only commands are the available evidence.

- [ ] **Step 5: Verify every route visually against the accepted mockup**

Use Browser/IAB first. Capture desktop screenshots at 1440x900 for all eight routes and mobile screenshots at 390x844 for New Alert, Alerts Overview, Doctor Alert, and Respond to Alert. The source is a 1536x1024 composite board rather than full-size standalone screen captures, so 1440x900 is the explicit implementation verification viewport.

Open the accepted concept and latest screenshots with `view_image`. Write a fidelity ledger covering at least:

1. sidebar width, logo/user placement, selected navigation, and simulation treatment;
2. exact page titles, progress labels, tabs, primary actions, and above-the-fold copy;
3. typography scale/weight/line height and control typography;
4. white background, gray borders, blue selection/action, and semantic red/amber/green;
5. form, table, clinician row, summary, timeline, dialog, and response-control geometry;
6. icon metaphor, stroke weight, optical size, and alignment;
7. desktop density and next-section visibility;
8. tablet/mobile stacking, drawer, card conversion, sticky actions, and overflow; and
9. core state changes across operator and doctor routes.

Fix every mismatch that would receive a design-review comment. Record only genuine intentional deviations, including the persistent SIMULATION indicator and explicit fictional wording required by repository safety rules.

- [ ] **Step 6: Update completion evidence and commit the verified frontend**

Mark only demonstrated items complete in `docs/product/definition-of-done.md`. Add the exact commands, test totals, viewport sizes, Browser/IAB method, `view_image` comparison, and intentional deviations. Then run `git status --short` and confirm no temporary screenshots, traces, or browser artifacts are staged.

```powershell
git add src/web tests/e2e docs/product/definition-of-done.md
git commit -m "test(web): verify frontend prototype workflows"
```

- [ ] **Step 7: Prepare the phase review handoff**

Report:

- files changed;
- architectural decisions;
- all commands and exact results;
- unit/E2E tests added and totals;
- accepted concept path and screenshot method;
- the nine inspected fidelity points and material mismatches fixed;
- remaining intentional deviations;
- limitations and unresolved `REQUIRES_HOSPITAL_DECISION` items;
- human actions required; and
- proposed final commit/tag message.

Stop for project-owner review. Do not push, tag, or begin backend integration without separate authorization.
