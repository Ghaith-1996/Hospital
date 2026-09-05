# Phase Definition of Done

## Phase 0: specification and repository rules

Phase 0 is done only when all of the following are true:

- [x] README and AGENTS.md state scope, authority, phase gate, and safety invariants.
- [x] Product decisions and workflow documents identify every missing hospital decision with `REQUIRES_HOSPITAL_DECISION`.
- [x] Simulation-only assumptions are explicitly labelled and cannot be mistaken for production rules.
- [x] Original source, transcription, structured suggestions, approved content, critical-number confirmation, and recipient confirmation are specified separately.
- [x] Delivered, opened, acknowledged, and responsibility accepted are defined as separate states.
- [x] Architecture documents describe the modular monolith, PostgreSQL boundary, outbox, directory integration boundary, and state machine.
- [x] ADRs record the major Phase 0 architectural and safety choices.
- [x] ADRs remain proposed/pending until the Phase 0 approval record is completed; mandatory user safety rules are clearly distinguished from project-owner and hospital decisions.
- [x] Threat model, data classification, logging policy, and production readiness gates exist.
- [x] Simulation fixtures permit fictional `555` phone values only, and any future real test endpoints are explicitly outside simulation and marked `REQUIRES_HOSPITAL_DECISION`.
- [x] The simulation contract defines deterministic fictional roles and test mechanics without creating production policy.
- [x] Delivery-state semantics distinguish supported, pending/not observed, occurred, failed, and `NotApplicable` states.
- [x] The Phase 1 implementation plan names files, interfaces, tests, commands, and review gates without adding code to the repository.
- [x] The Phase 1 plan has no cross-task test dependency ambiguity, uses exact dependency/image pinning, and distinguishes implementation tasks from the final review gate.
- [ ] A human reviews and approves the Phase 0 package.

The implementation and documentation checks above are complete. The final Phase 0 approval remains intentionally unchecked because it is an external project-owner/hospital action and cannot be self-approved by implementation work.

## Phase 0 non-goals

The following must not be present as Phase 0 implementation work:

- .NET solution or project files.
- Next.js application files.
- Database migrations or schema scripts.
- Docker Compose or runtime containers.
- Notification, speech, AI, directory, identity, or hospital integrations.
- Production policy values, escalation timings, real contact routes, or real data.

## Later phase gate pattern

Every later phase must state:

1. Files changed.
2. Architectural decisions.
3. Commands run and results.
4. Tests added and results.
5. Known limitations and unresolved human decisions.
6. Human actions required.
7. Proposed commit message.
8. A clear stop for review before the next phase.

## Phase 1: scaffold and local platform

Phase 1 is ready for human review only when the following are true:

- [x] The modular-monolith project graph, pinned toolchain, and empty project shells build successfully.
- [x] The simulation-mode web shell has no alert behavior or dispatch control.
- [x] Liveness and safe database-readiness endpoints exist without domain or notification behavior.
- [x] Local PostgreSQL and Testcontainers infrastructure use the pinned PostgreSQL 18 image and no Phase 1 schema.
- [x] Repository safety scanning, CI configuration, and dependency audits are present.
- [x] No secrets, real contact data, real clinical data, migrations, provider calls, or hospital production configuration were added.
- [x] Real PostgreSQL/Testcontainers verification is run after Docker Desktop is started by the project owner.
- [x] Playwright smoke verification is run after the pinned browser binary is installed in the local environment.
- [x] The project owner reviews this phase and explicitly approves or requests corrections before Phase 2.

## Phase 2: database and domain foundation

Phase 2 is ready for human review only when the following are true:

- [x] Domain entities, alert state machine, and safety constraints exist with unit tests.
- [x] EF Core mappings and a PostgreSQL 18 migration exist.
- [x] An empty database migrates and fictional demo seed data loads in Development/Test.
- [x] Demo reset refuses Staging and Production.
- [x] Organization isolation, optimistic concurrency, outbox atomicity, and idempotency uniqueness are tested against real PostgreSQL.
- [x] `user_roles` uniqueness is `UNIQUE (organization_id, user_id, role_id)`.
- [x] `alert_field_confirmations` stores one canonical confirmation per alert, draft version, and field identifier.
- [x] Acknowledgement and responsibility acceptance remain separate records.
- [x] No alert APIs, authentication, provider adapters, AI features, or hospital integrations were added.
- [x] The project owner reviewed this phase, requested uniqueness-constraint closure, and those corrections are included before Phase 3.

## Phase 3: development authentication and authorization

Phase 3 is ready for human review only when the following are true:

- [x] Fictional seeded identities can sign in through development authentication in Development/Test only.
- [x] Production and Staging refuse enabled development authentication at startup.
- [x] The UI shows `SIMULATION MODE` and `DEVELOPMENT AUTHENTICATION` banners and a seeded user switcher.
- [x] Arbitrary user IDs in request headers cannot select an identity.
- [x] Operator, Administrator, and Practitioner roles are authorized separately.
- [x] Organization scope checks reject a different organization ID.
- [x] No directory import, alert command, provider adapter, Entra SSO, or hospital integration was added.
- [x] Staging and Production fail closed when development authentication is enabled, and the user switcher is unmapped when it is disabled.
- [x] Client-supplied user IDs, organization IDs, and roles cannot impersonate a seeded identity.
- [x] The project owner reviewed this phase and requested the Phase 3 closure gate; those tests are included.

## Phase 4: fictional practitioner directory and CSV import

Phase 4 is ready for human review only when the following are true:

- [x] CSV is implemented as a directory **adapter**, not as the directory model.
- [x] Import validates, normalizes, detects duplicates, and previews before writing.
- [x] Practitioners are matched by `source_record_id`, then `simulation_code`, never by display name.
- [x] Similar names are disambiguated in search; inactive practitioners are not selectable; stale rows are flagged.
- [x] Operator/Administrator can search; only Administrator can import; Practitioner cannot.
- [x] Preview does not mutate; apply writes practitioners, roles, contacts, on-call, and source records.
- [x] No alert APIs, hospital directory connection, SCIM, Graph, FHIR, or production source mapping were added.
- [x] Production freshness, deactivation, merge, and source-of-truth rules remain `REQUIRES_HOSPITAL_DECISION`.
- [x] The CSV boundary rejects duplicate headers, malformed quotes, inconsistent row widths, non-UTC timestamps, and non-synthetic endpoint values without echoing protected values.
- [x] API authorization and organization scope are verified with unauthenticated, Operator, Administrator, and Practitioner negative cases; identity context remains server-derived.
- [x] The UI renders source/on-call synchronization timestamps and clears a preview when the selected CSV changes.
- [x] Fresh `scripts/test-all.ps1`, safety, and fresh-clone verification pass in the pinned environment before a human review and `phase-4` tag.

### Phase 4 closure evidence

On 2026-08-25, the pinned .NET SDK `10.0.100`, Node.js `24.16.0`, npm `11.13.0`, PostgreSQL `18.4`, and Playwright `1.55.1` verification completed successfully. `scripts/test-all.ps1` passed the safety scan, Release build, 118 backend tests, 9 web unit tests, typecheck, lint, and 1 Playwright smoke test. The same command passed from a clean clone of the reviewed commit. No production or hospital integration was added.

## Phase 5: simulation-only alert drafting

Phase 5 is ready for human review only when the following are true:

- [x] Operator and Administrator can create, read, and edit a protected typed simulation draft; Practitioner and unauthenticated identities cannot.
- [x] Typed source, synthetic patient reference, simulation location, urgency label, and all four SBAR fields are validated server-side.
- [x] Organization and user identity come only from the authenticated server principal, and a real foreign-organization alert ID is returned as not found.
- [x] Typed source and SBAR content remain protected at rest and API responses do not expose ciphertext or protected-value internals.
- [x] Original operator-entered source remains separately protected and persisted from structured SBAR content; normalization and critical-field confirmation do not overwrite it.
- [x] Every draft edit supplies the expected version, increments the draft version, and invalidates prior confirmation state.
- [x] Source, SBAR, critical-value, and unit edits recreate the current version's critical fields as unresolved; earlier confirmations remain historical only.
- [x] Stale edit, critical-field confirmation, and submission commands return safe conflicts with compose-page recovery guidance.
- [x] Every recorded critical number and unit remains unresolved until explicitly confirmed by an authenticated human for the exact value, unit, and current draft version.
- [x] A draft cannot advance to `PendingConfirmation` with missing required content or unresolved critical fields.
- [x] Synthetic sentinel tests show general API logs and safe errors do not contain the patient reference, typed source, or complete SBAR payload.
- [x] Negative authorization tests cover allowed Operator edits, Practitioner impersonation attempts, anonymous access, foreign-organization read/update attempts, and ignored client-supplied organization context.
- [x] The compose UI supports create/edit, version display, critical-field confirmation, and submit-for-confirmation without recipient or dispatch controls.
- [x] No Phase 5 endpoint or application service can advance an alert beyond `PendingConfirmation` to `DispatchQueued`.
- [x] PostgreSQL integration tests cover Operator, Administrator, Practitioner, unauthenticated, organization-scoping, validation, protected-content, and concurrency behavior.
- [x] No Phase 6 recipient selection, Phase 7 dispatch, provider adapter, hospital connection, SCIM, Graph, FHIR, AI, speech, or production identity behavior was added.
- [x] Fresh `scripts/test-all.ps1`, repository safety, `git diff --check`, and scope review pass in the pinned environment before human review.
- [x] The project owner reviews and approves Phase 5 before a `phase-5` tag or any Phase 6 work.

### Phase 5 closure evidence

On 2026-08-27, `scripts/test-all.ps1` passed the sensitive-data scan, Release build with zero warnings and errors, 147 backend tests, 16 web unit tests, typecheck, lint, and 1 Playwright smoke test. The focused PostgreSQL Phase 5 API suite passed 13 tests, including exact version/value/unit confirmation, critical-value and unit edit invalidation, stale-command conflicts, source/SBAR preservation, log non-disclosure sentinels, positive Operator and Administrator access, Practitioner and anonymous denial, persisted foreign-organization read/update denial, ignored client-supplied organization context, and proof that Phase 5 endpoints stop at `PendingConfirmation`. No production or hospital integration was added.

The project owner approved Phase 5 on 2026-08-27. Commit `3d8bc56` was fast-forwarded to `main`, pushed to GitHub, and tagged `phase-5`.

## Phase 6: recipient selection and exact review

Phase 6 is ready for human review only when all of the following are true:

- [x] Operator and Administrator can replace the complete recipient set from the authenticated organization's fictional directory; Practitioner and unauthenticated identities cannot.
- [x] Recipient selection is manual only. No AI, ranking, default, on-call rule, background task, or server process selects a practitioner.
- [x] Each selected recipient records the exact alert draft version, practitioner, optional role, channel, selecting user, selection time, safe directory revision, source timestamp, and displayed on-call snapshot.
- [x] A recipient edit increments `DraftVersion` once for the full replacement, invalidates earlier critical-field confirmations, and makes stale commands return RFC 7807 conflict/reload guidance.
- [x] Inactive, foreign-organization, duplicate, channel-ineligible, and changed-directory selections are rejected without exposing contact values.
- [x] The operator-approved message is protected and persisted separately from original source and structured SBAR content; editing it increments `DraftVersion` and invalidates earlier confirmations.
- [x] The review response and page show one exact version containing synthetic patient reference, location, urgency, approved message, confirmed critical values and units, recipients, channels, directory timestamps/on-call labels, and `DEMO` policy versions.
- [x] Confirmation requires an authenticated authorized human, the exact reviewed draft version, and an `Idempotency-Key`.
- [x] Confirmation, sanitized audit, idempotency result, state transition, and one identifier-only outbox item are committed atomically; no Phase 6 code processes that item or calls a provider.
- [x] Repeating the same confirmation key and request returns the original result without a duplicate state transition, audit event, or outbox item; reusing the key for a different request returns a safe conflict.
- [x] Clinical content, patient content, approved message, recipient contact values, and complete request payloads do not appear in general logs, exceptions, audit metadata, idempotency records, or outbox payloads.
- [x] PostgreSQL integration tests cover authorization, organization isolation, version conflicts, directory revision conflicts, recipient snapshots, idempotency races, atomic rollback, and identifier-only outbox contents.
- [x] The web flow includes dynamic compose, recipients, and review routes with deliberate confirmation and double-submission protection; the live/dispatch screen remains unavailable.
- [x] At the Phase 6 boundary, no worker, provider adapter, delivery attempt, retry, callback, acknowledgement, responsibility acceptance, escalation, hospital connector, or production identity behavior was included.
- [x] Fresh format, build, test, typecheck, lint, PostgreSQL integration, browser, sensitive-data, and scope checks pass before human review.
- [x] The Phase 6 boundary was accepted as the prerequisite to Phase 7 implementation; Phase 7 has its own review gate below.

### Phase 6 implementation evidence

On 2026-08-28, the pinned .NET 10.0.100 Release build passed with zero warnings and errors. The functional backend suite passed 160 tests (42 domain, 25 application, 64 API integration, and 29 infrastructure), including PostgreSQL/Testcontainers coverage for recipient snapshots, authorization, organization isolation, directory revisions, idempotency, rollback, and identifier-only outbox contents. The web suite passed 17 unit tests, typecheck, lint, production build, and 1 Playwright browser smoke test. The sensitive-data scan, `git diff --check`, and Phase 5 boundary scan passed.

The host does not have the .NET SDK, so `scripts/test-all.ps1` was not run directly; its pinned-container build and test stages plus the host Node checks were run individually. The full backend solution test also includes three architecture tests that cannot pass from this Windows linked worktree mounted in Linux: two project-graph checks interpret Windows project-reference separators as part of the project name, and the tracked-file safety check cannot run `git ls-files` through the linked-worktree `.git` file. The changed-file `dotnet format` check has the same environment limitation because the repository's Windows encoding/path baseline is evaluated in the Linux container. These limitations remain open for owner review; no phase approval or tag is claimed.

## Phase 7: simulated dispatch worker

Phase 7 is ready for project-owner review only when all of the following are true:

- [x] The worker fails closed unless simulation dispatch is enabled explicitly in `Development` or `Test`; `Staging` and `Production` reject the enabled setting at startup.
- [x] The worker claims pending and expired-lease outbox rows with PostgreSQL locking, records owner/expiry, and permits only the current owner to complete, retry, or fail a row.
- [x] The outbox payload remains strict identifier-only data and is reconciled to the organization-scoped confirmed alert/version before processing.
- [x] SecureMessage, SMS, and Voice use typed in-process simulation ports with deterministic fictional scenarios and no network/provider SDK.
- [x] Delivery attempts use stable per-recipient/channel attempt keys; provider events are unique by organization and provider event ID; duplicate and out-of-order events do not regress status.
- [x] Provider-outage and delayed scenarios use bounded retry/backoff; invalid work becomes a visible terminal failure; worker restart/expired-lease behavior is covered by regression tests.
- [x] Scenario controls are Administrator-only, organization-scoped, and unavailable outside Development/Test; caller-supplied organization/user/role fields are ignored.
- [x] Delivery status is organization-scoped and returns only operational fields, not protected message/contact values or raw provider metadata.
- [x] The worker preserves the distinction between submitted, delivered, opened, acknowledged, responsibility accepted, and resolved; no response, live screen, or escalation behavior is added.
- [x] Documentation records the simulation boundary, lease/retry/idempotency model, safe logging/status fields, and `REQUIRES_HOSPITAL_DECISION` production choices.
- [x] Pinned .NET restore, Release build, focused domain/application/adapter tests, and compile-time API/worker regression coverage pass in the current worktree.
- [x] PostgreSQL/Testcontainers worker, persistence, and API integration tests pass with a reachable Docker engine.
- [x] Full `scripts/test-all.ps1`, frontend/browser checks, sensitive-data scan, scope scan, and fresh-clone migrate/seed verification pass.
- [x] The project owner reviews Phase 7 and separately authorizes the pushed implementation commit; tag creation remains a separate action.

### Phase 7 implementation evidence

On 2026-08-29, the pinned .NET 10.0.100 locked restore and Release solution build passed with zero warnings and errors. Focused domain, application, and simulation-adapter tests passed (6, 10, and 6 respectively). The complete backend solution passed 204 tests (9 architecture, 48 domain, 34 application, 42 infrastructure, and 71 API), including PostgreSQL/Testcontainers worker, persistence, authorization, organization isolation, lease recovery, retry, duplicate, and out-of-order coverage. The prescribed `scripts/test-all.ps1` passed its sensitive-data scan, Release build, backend tests, web 17-test suite, typecheck, lint, and Playwright 1-test browser smoke. A fresh clone of the reviewed content applied all migrations through `20260829234957_Phase7SimulatedDispatch` and seeded 3 fictional users and 12 fictional practitioners against an isolated PostgreSQL 18.4 container. The implementation was finalized and pushed as commit `45f4024`; no `phase-7` tag is currently recorded.

## Phase 8: simulation practitioner response and closed loop

Phase 8 is ready for project-owner review only when all of the following are true:

- [x] A user-to-practitioner link is explicit, organization-scoped, server-resolved, and never inferred from display name or simulation handle.
- [x] A mapped Practitioner sees only confirmed Active alerts addressed to the linked practitioner; cross-organization and unaddressed alerts are non-disclosing.
- [x] SecureMessage opened state, acknowledgement, call-unit request, terminal disposition, responsibility assignment, and alert lifecycle remain distinct.
- [x] Acknowledgement never creates responsibility; acceptance creates one durable assignment, and operator resolution requires that assignment for the exact confirmed version.
- [x] Declined and unavailable remain visible and do not trigger Phase 9 escalation.
- [x] One acknowledgement and one terminal disposition per practitioner/alert/version are enforced under concurrent requests.
- [x] Side-effecting commands require idempotency keys and atomically persist response state, optional responsibility, idempotency result, and sanitized audit metadata.
- [x] Practitioner inbox/detail and Operator/Administrator live projections derive identity and organization from the authenticated server principal and expose no raw contact/provider/protected storage values.
- [x] The web UI visibly distinguishes opened, acknowledged, accepted, declined, unavailable, delivery failure, and not-applicable states without color-only communication.
- [x] Simulation response and lifecycle endpoints fail closed outside Development/Test; no real provider, callback, automated escalation, production resolution/transfer, hospital integration, AI, or Phase 9 behavior is added.
- [x] Call-unit requests are allowlisted, non-terminal, idempotent simulation records and never contact a real unit.
- [x] Operator Resolve and Cancel actions are exact-version, organization-scoped, idempotent commands with server-side role and responsibility preconditions.
- [x] Delivery failures remain visible and render a non-routing manual-fallback placeholder marked `REQUIRES_HOSPITAL_DECISION`.
- [x] Patient references are protected ciphertext at rest, and source edits append immutable protected source revisions rather than replacing history.
- [x] Recipient selections persist `selectionSource` provenance, currently `Manual`, with future expansion values explicit in the domain contract.
- [x] API routes are under `/api/v1`, OpenAPI is generated as 3.1.x, API requests are rate-limited and size-bounded, and old unversioned routes are not mapped.
- [x] CI includes formatting, OpenAPI contract verification, dependency vulnerability checks, web audit, and API/worker/web container builds.
- [x] Full backend, PostgreSQL/Testcontainers, web, browser, safety, and fresh-source migrate/seed verification pass for the corrected tree before push.
- [x] The project owner authorizes review, correction of findings, and commit/push after successful verification on 2026-09-05; no tag is created by implementation work.

### Phase 8 implementation evidence

On 2026-08-30, the exact .NET 10.0.100, Node.js 24.16.0, and npm 11.13.0 pins were used for fresh verification. The prescribed `scripts/test-all.ps1` passed the sensitive-data scan, Release build with zero warnings and errors, 237 backend tests (9 architecture, 57 domain, 39 application, 48 infrastructure, and 84 API), 22 web unit tests, typecheck, lint, and 2 Playwright browser flows. The pinned production web build also passed, and rendered desktop/mobile browser checks verified the simulation boundary, navigation, unavailable-API fallback, and responsive layout.

A fresh detached worktree reproduced the complete Phase 8 working-tree patch over commit `45f4024`. An isolated PostgreSQL 18.4 container applied migrations through `20260830160849_Phase8PractitionerResponses`, then seeded 3 fictional users, 12 fictional practitioners, 1 explicit user-to-practitioner link, and 5 protected fictional contact endpoints. Focused fresh-tree tests passed for the Phase 8 domain (9), environment guard (5), PostgreSQL persistence (6), and response/live API behavior (13). `git diff --check` and the scope scan passed. No commit, push, or tag is claimed; those remain separate owner actions.

The final diff review on 2026-09-02 corrected strict response/reason-code validation, idempotency-key normalization, SecureMessage-only opened observations, and `NotApplicable` web rendering, with regression coverage added. The compliance-correction pass on 2026-09-02 added protected patient-reference migration, immutable source revisions, selection provenance, call-unit response handling, exact operator lifecycle actions, safe fallback display, API v1/OpenAPI/rate-limit/body-size controls, expanded simulation role names, and CI/container gates. Current local verification passes formatting, locked restore, Debug compilation, focused Domain/Application/API-hardening/safety tests, web unit tests, typecheck, lint, OpenAPI contract verification, sensitive-data scan, and `git diff --check`. PostgreSQL/Testcontainers lifecycle and persistence suites and container builds remain to be run with Docker Desktop available; Playwright and the full release `scripts/test-all.ps1` run also remain pending in this host. Human Phase 0/6/8 approval and GitHub branch/ruleset enforcement remain external review actions. No production provider, callback, hospital integration, automated escalation, or Phase 9 behavior was added.

## Compliance-correction review record

- [x] Protected patient-reference storage and migration are implemented and covered by schema/migration tests.
- [x] Source edits preserve immutable protected revisions keyed by alert draft version.
- [x] Call-unit requests, approved decline reasons, operator resolve/cancel actions, and manual-fallback display are implemented with regression coverage.
- [x] Recipient selection provenance, expanded simulation role names/policies, API v1 routing, OpenAPI 3.1 verification, rate limits, request limits, dependency checks, and container build definitions are present.
- [x] The local formatter and non-Docker focused verification pass.
- [x] Docker-backed PostgreSQL/Testcontainers, container image builds, full release verification, and pinned Playwright verification are complete on a host with the required services.
- [x] The project owner authorizes correction of review findings and publication after successful verification.
- [ ] GitHub rulesets/branch protection are configured by a repository administrator.

### 2026-09-05 review and verification

This verification supersedes the outstanding local checks recorded on 2026-09-02. The review corrected shared EF ownership of source revisions, response/lifecycle races, the globally shared API request budget, and the web container's compiled API destination. Full-suite execution additionally corrected source-version collision handling, the API fixture's reset-safe database name, a typed-ID query in a lifecycle test, and shared-fixture rate-limit interference. The web image now includes root build dependencies, and container verification exercises its real proxy against a separate synthetic HTTP fixture.

Fresh `scripts/test-all.ps1` execution passed using .NET SDK 10.0.100, Node.js 24.16.0, npm 11.13.0, and Docker Desktop with PostgreSQL 18.4 test containers. Evidence:

- Locked backend restore, formatting verification, and Release build passed with zero build warnings/errors.
- All 273 backend tests passed: 60 domain, 39 application, 9 architecture, 67 infrastructure, and 98 API integration tests; none were skipped.
- Backend dependency scanning found no vulnerable packages, and the web production-dependency audit found zero vulnerabilities.
- All 24 web tests, TypeScript checking, ESLint, and both Chromium browser flows passed.
- API, worker, and production web Docker image builds passed. `scripts/verify-web-container.ps1` confirmed that the built web image preserves the API path/query when proxying to a separate container.
- The sensitive-data scan, OpenAPI contract verifier, and `git diff --cached --check` passed.
- A new local clone checked out the exact staged source tree `6c360d435a4f4463363161be2974d28ce08a9832`, without local build artifacts. Locked restore and three focused PostgreSQL tests passed for empty-database migration/seeding, legacy plaintext upgrade, and missing-key recovery. The temporary checkout and test containers were removed afterward.

Regression evidence includes failures before each behavior fix, followed by passing persisted source-history tests, five deterministic PostgreSQL lifecycle/response races, caller-budget isolation tests, and the container proxy check. Stale source writes are now safe concurrency conflicts for both synchronous and asynchronous saves, with winning history retained and failed-writer audit data rolled back. Independent final code review found no remaining actionable blockers in the combined corrections.

The reviewed change set covers the existing Phase 8 domain/persistence, application/API, web, tests, CI/container, and documentation corrections. Intended commit message: `feat: complete Phase 8 simulation compliance corrections`. No production deployment, external provider, hospital integration, Phase 9 behavior, or tag is included. Production and hospital approval gates remain `REQUIRES_HOSPITAL_DECISION`; repository ruleset administration remains a human action.

## Frontend Prototype Phase: redesign review gate

The approved frontend redesign is ready for review only when all of the following are true:

- [x] All nine visual states and eight routes are present and connected.
- [x] The supplied mockup's design system and page anatomy are faithfully reproduced.
- [x] The operator and doctor workflows update one persistent fictional state model.
- [x] The fictional-user switcher and reset action work.
- [x] Directory, Reports, and Settings are clearly disabled or show the approved Coming later state.
- [x] Mobile and tablet layouts remain usable.
- [x] Loading, empty, validation, error, and not-found states are implemented.
- [x] Each screen presents one obvious primary action and preserves the visible simulation treatment.
- [x] All navigation and controls are keyboard operable with visible, consistent focus indicators.
- [x] Fields, dialogs, tables, and mobile-card equivalents preserve semantic labels and accessible descriptions.
- [x] Status, urgency, and response meaning are conveyed textually in addition to color.
- [x] Practical interactive targets are approximately 44 pixels.
- [x] Doctor response controls, response summaries, and escalation rendering are explicitly marked `SIMULATION_ONLY_ASSUMPTION` in product documentation and remain local mock state only.
- [x] Real doctor response workflow authority, escalation policy values, escalation intervals, recipient-selection policy, privacy conclusions, and clinical recommendations remain `REQUIRES_HOSPITAL_DECISION`.
- [x] No backend, API, database, notification, response processor, or escalation engine change is included.
- [x] Frontend tests, typecheck, lint, build, browser verification, safety checks, and visual comparison pass.
- [x] Remaining intentional deviations from the supplied mockup are documented for human review.

### Frontend Prototype Task 11 verification evidence

On 2026-09-03, Task 11 fix-round verification rechecked the frontend-only redesign against `docs/design/frontend-prototype-nine-screen-mockup.png`. The accepted mockup and regenerated desktop/mobile contact sheets were opened with `view_image`. Playwright-driven screenshots were captured from the actual local Next dev server and saved outside the repository under `C:\Users\ghait\AppData\Local\Temp\task-11-review-screenshots-20260903-002254`.

Desktop screenshots were captured at 1440x900 for New Alert, Review & Confirm, Alert Sent, Alerts Overview, Alert Details, Doctor Inbox, Doctor Alert, Respond to Alert, and fixed Escalation Progress. Mobile screenshots were captured at 390x844 for New Alert, Alerts Overview, Doctor Alert, and Respond to Alert. The capture manifest asserted the expected URL and `h1`, exactly one `h1`, no not-found content, and no page-level horizontal overflow for all 13 screenshots. Dr. Marc localStorage state was seeded deterministically for Doctor Inbox, Doctor Alert, and Respond to Alert captures.

Fidelity ledger:

1. Sidebar/topbar: desktop sidebar remains visible and the useless desktop Close control is hidden; mobile uses a Menu button and modal drawer with hidden/inert closed state, focus containment while open, and focus return to Menu.
2. Page titles, progress labels, tabs, primary actions, and above-the-fold copy were checked across the nine captured desktop states; the doctor inbox preserves the mockup-visible labels `Chest pain, hypotension`, `Respiratory distress`, and `Suspected sepsis`.
3. Typography scale, weight, line height, and control typography remain consistent across forms, tables, cards, detail panels, and response screens.
4. White background, gray borders, blue selection/action treatment, and semantic red/amber/green status colors are preserved with textual labels.
5. Form, table, clinician row, summary, timeline, dialog, and response-control geometry match the approved structure; mobile table information converts to equivalent cards.
6. Icon metaphor, stroke weight, optical size, and alignment were checked in the sidebar, mobile topbar, status markers, empty/not-found states, and escalation timeline.
7. Desktop density remains scannable at 1440x900, with detail pages retaining side summaries and response summaries consistent with the concept.
8. Responsive behavior was checked at 390x844 and 768px: the New Alert form/summary stack, detail grids stack, cards replace tables, long fictional references wrap, response actions remain visible without covering content, and page-level horizontal overflow is absent.
9. Core state changes were verified end-to-end: operator create/review/confirm/send/open details; Dr. Marc acknowledgement remains distinct from acceptance; fixed escalation content remains `DEMO elapsed time: 12 min` with no automatic transition; rapid local actions preserve in-memory and localStorage state.

Intentional deviations from the mockup remain limited to repository safety requirements: persistent `SIMULATION` badges, explicit fictional/local/no-real-notification copy, `SIMULATION_ONLY_ASSUMPTION` documentation for doctor responses and escalation rendering, and disabled/Coming later treatment for out-of-scope areas. No real dispatch, delivery, acknowledgement processor, escalation engine, provider integration, backend API, database, or infrastructure change is included.

Verification commands:

- `npm --prefix src/web test -- --run`: passed, 9 test files and 71 tests.
- `npm --prefix src/web run typecheck`: passed.
- `npm --prefix src/web run lint`: passed with zero warnings.
- `npm --prefix src/web run build`: passed; Next compiled successfully and generated 8 static pages plus dynamic app routes.
- `npm run web:e2e`: passed, 4 Playwright workflows and 4 tests in 6.7s. The final config refuses existing servers, starts the frontend from `src/web` on deterministic `http://127.0.0.1:3101`, and records `reuseExistingServer: false` in the E2E server metadata. The implementation uses shell-free Playwright global setup/teardown because Playwright's Windows `webServer` shell lifecycle hung after completed tests on this host.
- `powershell -ExecutionPolicy Bypass -File scripts/verify-no-sensitive-data.ps1`: passed.
- `git diff --check`: passed; only CRLF conversion warnings were reported.
- `rg -n "fetch\(|/api/|setInterval|setTimeout|XMLHttpRequest|EventSource|WebSocket" src\web\app src\web\components src\web\features`: no matches, confirming no active frontend route/component/store fetch, API, or timer calls.

`scripts/test-all.ps1` was not run on this host because `global.json` requires .NET SDK `10.0.100`, while the host only reports SDK `9.0.310`. Frontend-only gates above are the available local evidence.

## Integration with Phase 8 backend baseline

The frontend prototype replaces the old backend-connected screens and their UI tests. Backend implementation, migrations, API integration tests, data-protection checks, container support, and historical Phase 8 verification evidence are retained from main. The local prototype suite covers the replacement workflows; it does not claim browser-to-API integration. The legacy live-status route redirects to local alert details, and the API proxy retains the versioned `/api/v1` boundary.
