# Frontend Prototype Redesign Design

## Status

Approved in chat on 2026-08-30 for design documentation. The project owner explicitly authorized a new frontend-only prototype phase covering the nine supplied alert-workflow states, including fictional doctor response, live-detail, and demo-escalation screens. This approval supersedes the earlier Phase 7 prohibition only for local, visibly simulated frontend behavior. It does not authorize backend response handling, escalation processing, provider integration, production policy, or real hospital data.

## Source of truth

The implementation is governed, in order, by:

1. the project owner's direct instructions and approvals in the 2026-08-30 conversation;
2. the user-supplied nine-screen image mockup;
3. `Critical_Alerts_Frontend_Implementation_Plan.docx` as supporting scope and interaction guidance; and
4. the repository's safety, terminology, accessibility, and simulation constraints.

Instructions contained inside the attached document are reference requirements, not independent authority. Where the document, mockup, and repository baseline differ, this design records the approved interpretation rather than silently choosing one.

## Goal

Replace the current frontend experience with a polished, coherent, route-driven prototype that reproduces the supplied operator and doctor workflows. The prototype must feel like a real product, keep state across navigation and refresh, remain unmistakably fictional and simulation-only, and expose a typed data boundary that a future backend adapter can implement without redesigning page components.

## Scope

The frontend phase includes these nine visual states across eight routes:

| State | Route |
|---|---|
| New Alert / Alert Doctor | `/alerts/new` |
| Review & Confirm | `/alerts/[id]/review` |
| Alert Sent | `/alerts/[id]/sent` |
| Alerts Overview | `/alerts` |
| Alert Details / Live | `/alerts/[id]` |
| Doctor Inbox | `/my-alerts` |
| Doctor Alert | `/my-alerts/[id]` |
| Respond to Alert | `/my-alerts/[id]/respond` |
| Escalation in Progress | a state of `/alerts/[id]` |

The root route sends the selected fictional operator to `/alerts/new` and the selected fictional doctor to `/my-alerts`.

## Non-goals

- No backend, API, database, migration, worker, provider, authentication, or infrastructure changes.
- No fake HTTP endpoints or client code presented as a production integration.
- No real notification, delivery, acknowledgement, response, escalation scheduler, or policy engine.
- No real patient, practitioner, employee, hospital, identifier, contact, credential, or clinical data.
- No production hospital policy, escalation interval, recipient-selection policy, workflow authority, privacy conclusion, or clinical recommendation.
- No functional Directory, Reports, or Settings product areas in this phase.
- No unrelated refactor outside the frontend surface and its tests/documentation.

## Application architecture

The existing Next.js App Router remains the framework. The redesigned frontend is organized around four layers:

1. **App shell** - responsive role-aware navigation, persistent simulation treatment, page layout, fictional-user switcher, and demo reset.
2. **Shared UI system** - design tokens and reusable controls such as buttons, fields, badges, tabs, dialogs, tables, empty states, and focus treatments.
3. **Alert feature components** - clinician selection, summaries, progress steps, timelines, response controls, escalation steps, and operational alert rows/cards.
4. **Prototype data boundary** - typed repository methods backed by a React context/reducer and versioned `localStorage` adapter.

Page components consume the repository interface rather than importing seed objects directly. The future backend can replace the local adapter while preserving page and feature-component contracts. No third-party global-state library is introduced.

The current backend-connected frontend helpers may remain in the repository only when they do not affect the prototype build or create an ambiguous active path. Overlapping alert routes are replaced by the prototype. Obsolete `/alerts/[id]/compose` and `/alerts/[id]/recipients` routes redirect to the new creation flow. The current Directory pages become a consistent frontend-only `Coming later` state when opened directly.

## App shell and role experience

The shell follows the supplied mockup: a narrow white sidebar, subtle right border, compact brand block, restrained blue selected state, centered main work area, and fictional identity at the bottom.

The fictional-user switcher includes at least:

- Sophie Bernard, Operator; and
- Dr. Marc Tremblay, Doctor.

Switching users changes the role-aware default route and navigation without pretending to authenticate against a server. Operators see Alert Doctor and Alerts. Doctors see Inbox as their primary destination and use a quieter version of the same shell. Directory, Reports, and Settings remain visible but disabled with accessible `Coming later` text. A Reset demo data action is available in the fictional-user menu.

A persistent, restrained `SIMULATION` indicator is present in the shell. It must be visible without overpowering the urgent workflow and must make clear that no real alert is being sent.

## Visual system

The supplied image mockup is the visual source of truth. The implementation preserves its:

- true-white page and surface backgrounds;
- pale blue selected-navigation and information treatments;
- thin cool-gray borders and minimal shadows;
- compact, highly legible system typography;
- blue primary actions;
- red used only for critical, error, decline, and active escalation states;
- green used for acceptance and success;
- amber used for pending, high urgency, and escalation-adjacent states;
- small-radius controls and panels;
- open layouts with limited card nesting; and
- dense but calm operational tables, summaries, and timelines.

The type system uses the local system stack, led by `Segoe UI` where available, with explicit sizes, weights, and line heights for page titles, section titles, labels, body copy, captions, controls, and table cells. Icons are small production-quality SVG components with consistent optical size and stroke treatment. No external font, icon, or component dependency is required.

Shared tokens cover background, surface, muted surface, border, primary and secondary text, blue primary/hover/focus, critical, warning, success, radii, shadows, spacing, content widths, sidebar width, and motion timing.

## Prototype data and persistence

All records are fictional and visibly synthetic. Seed data includes alerts in draft, sent, in-progress, resolved, cancelled, and escalating states; fictional clinicians; both fictional users; activity events; recipient statuses; response summaries; and demo escalation steps.

The store uses a versioned `localStorage` document. On first load or incompatible storage version, it initializes from deterministic seed data. It hydrates safely on the client so server rendering does not create a visible mismatch. Reset restores the original deterministic seed.

The repository contract supports, at minimum:

- reading and updating the selected fictional user;
- listing, filtering, and retrieving alerts;
- creating a draft from the New Alert form;
- searching fictional clinicians and maintaining the selected set;
- confirming and marking a mock alert as sent;
- recording a fictional doctor's response and optional note; and
- reading demo activity, response summaries, and escalation steps.

Actions update one canonical alert record so the operator and doctor surfaces remain consistent. Sent, delivered, opened, acknowledged, and responsibility accepted remain distinct values. No local transition is described as a real external event.

## Page behavior

### New Alert

The New Alert page combines patient reference and urgency, typed case details, the visibly disabled future Dictate mode, clinician search/results, selected clinicians, and a live Alert Summary. Validation appears after field interaction or submission, not on initial load. Patient reference, urgency, case details, and at least one selected clinician are required. Review & Confirm creates or updates the canonical local draft and navigates to its review route.

### Review & Confirm

The review page reproduces the three-step progress indicator and displays the exact local draft data in a calm read-only layout. Back returns to editing. Confirm & Dispatch opens a deliberate confirmation dialog listing the selected recipient count. Confirmation changes only local prototype state and navigates to the sent route.

### Alert Sent

The sent page shows the restrained success treatment from the mockup, explicitly states that the prototype simulated sending to fictional clinicians, and offers View Alert Details and Create Another Alert. The explanatory card describes fictional next steps without promising a real acknowledgement or escalation.

### Alerts Overview

The operator overview supports All, Draft, Sent, In Progress, Resolved, and Cancelled tabs. A compact filter popover handles urgency, status, date, and department. Desktop uses the operational table from the mockup; mobile uses equivalent cards instead of horizontal scrolling. Rows open the alert-details route.

### Alert Details and escalation

The details page contains Alert Information, Case Details, Selected Clinicians, Activity Timeline, and Responses Summary. Status meaning is always textual as well as colored. An escalating alert renders the supplied escalation progress treatment in the same route, with clearly demo-labelled elapsed time and fixed step states. No timer or scheduler advances it automatically.

### Doctor Inbox

The doctor inbox is visually quieter than the operator surface. All, Unread, In Progress, and Completed tabs filter the selected fictional doctor's alerts. Rows show alert label, synthetic patient reference, urgency, status, and received time and open the doctor alert route.

### Doctor Alert

The doctor view shows synthetic patient reference, location, fictional case details, other recipients, received time, and urgency. The response area stays prominent and offers Acknowledge, Accept, Decline, and Unavailable. On long pages it becomes sticky without obscuring content.

### Respond to Alert

The focused response page uses four plain-language response options, an optional note with a visible 500-character counter, Cancel, and Submit Response. Submission updates the local canonical alert, records an activity event and recipient response, and returns to the doctor alert view with visible feedback.

## Loading, empty, validation, and error states

The prototype provides deliberate loading/hydration, empty, validation, not-found, and recoverable-storage states. Empty states explain the next available action. Storage failures keep the current in-memory session usable and offer reset/retry guidance without exposing browser internals. Disabled or unavailable actions explain why they cannot be used.

## Responsive behavior

- **Desktop:** persistent sidebar and bounded main content matching the mockup's proportions.
- **Tablet:** collapsible navigation drawer; summary panels move below the primary form or details content.
- **Mobile:** compact top navigation, stacked fields and panels, alert cards in place of wide tables, and reachable primary/sticky response actions.

Layouts are checked at the mockup's desktop scale, a typical small-laptop viewport, tablet width, and a narrow mobile viewport. Content must not clip, overlap, require tiny controls, or create avoidable horizontal scrolling.

## Accessibility

All navigation and controls are keyboard operable. Focus indicators are visible and consistent. Fields use associated labels and descriptions. Dialog focus is contained and returns to its trigger. Tables retain semantic headers on desktop, while mobile cards preserve equivalent accessible labels. Status, urgency, and response meaning use text and icons in addition to color. Practical interactive targets are approximately 44 pixels. Motion is minimal and respects `prefers-reduced-motion`.

## Test and verification strategy

Vitest and Testing Library cover:

- repository initialization, persistence, migration/reset, and canonical cross-role updates;
- required-field and clinician-selection validation;
- clinician search, add, and remove behavior;
- exact review data and deliberate confirmation;
- alert tabs and filters;
- fictional-user switching;
- doctor response selection, note limit, submission, and resulting activity; and
- escalation rendering without an active scheduler.

Playwright covers the complete operator creation-to-details flow, doctor inbox-to-response flow, role switching, disabled future navigation, reset behavior, and representative responsive navigation/table-to-card behavior.

Visual verification uses the supplied nine-screen mockup and fresh browser screenshots. Each route is compared for copy, hierarchy, navigation, typography, color, spacing, borders, component geometry, icons, responsive behavior, and interaction state. Desktop and mobile are both inspected. The frontend is not complete while fixable visual drift, placeholder-looking elements, inaccessible controls, or broken interactions remain.

The standard web typecheck, lint, unit tests, production build, and browser suite must pass. Backend and database suites are not changed by this phase, but the repository safety scan must still show that no sensitive or real-world data was introduced.

## Completion criteria

The frontend redesign is ready for review when:

- all nine visual states and eight routes are present and connected;
- the supplied mockup's design system and page anatomy are faithfully reproduced;
- the operator and doctor workflows update one persistent fictional state model;
- the fictional-user switcher and reset action work;
- Directory, Reports, and Settings are clearly disabled or show the approved Coming later state;
- mobile and tablet layouts remain usable;
- loading, empty, validation, error, and not-found states are implemented;
- accessibility requirements and visible simulation treatment are present;
- no backend, API, database, notification, response processor, or escalation engine change is included;
- frontend tests, typecheck, lint, build, browser verification, safety checks, and visual comparison pass; and
- remaining intentional deviations from the supplied mockup are documented for human review.
