# Phase Definition of Done

## Phase 0: specification and repository rules

Phase 0 is done only when all of the following are true:

- [ ] README and AGENTS.md state scope, authority, phase gate, and safety invariants.
- [ ] Product decisions and workflow documents identify every missing hospital decision with `REQUIRES_HOSPITAL_DECISION`.
- [ ] Simulation-only assumptions are explicitly labelled and cannot be mistaken for production rules.
- [ ] Original source, transcription, structured suggestions, approved content, critical-number confirmation, and recipient confirmation are specified separately.
- [ ] Delivered, opened, acknowledged, and responsibility accepted are defined as separate states.
- [ ] Architecture documents describe the modular monolith, PostgreSQL boundary, outbox, directory integration boundary, and state machine.
- [ ] ADRs record the major Phase 0 architectural and safety choices.
- [ ] ADRs remain proposed/pending until the Phase 0 approval record is completed; mandatory user safety rules are clearly distinguished from project-owner and hospital decisions.
- [ ] Threat model, data classification, logging policy, and production readiness gates exist.
- [ ] Simulation fixtures permit fictional `555` phone values only, and any future real test endpoints are explicitly outside simulation and marked `REQUIRES_HOSPITAL_DECISION`.
- [ ] The simulation contract defines deterministic fictional roles and test mechanics without creating production policy.
- [ ] Delivery-state semantics distinguish supported, pending/not observed, occurred, failed, and `NotApplicable` states.
- [ ] The Phase 1 implementation plan names files, interfaces, tests, commands, and review gates without adding code to the repository.
- [ ] The Phase 1 plan has no cross-task test dependency ambiguity, uses exact dependency/image pinning, and distinguishes implementation tasks from the final review gate.
- [ ] A human reviews and approves the Phase 0 package.

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
- [ ] Fresh format, build, test, typecheck, lint, PostgreSQL integration, browser, sensitive-data, and scope checks pass before human review.
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
- [ ] The project owner reviews Phase 7 and separately authorizes any commit/tag/push action.

### Phase 7 implementation evidence

On 2026-08-29, the pinned .NET 10.0.100 locked restore and Release solution build passed with zero warnings and errors. Focused domain, application, and simulation-adapter tests passed (6, 10, and 6 respectively). The complete backend solution passed 204 tests (9 architecture, 48 domain, 34 application, 42 infrastructure, and 71 API), including PostgreSQL/Testcontainers worker, persistence, authorization, organization isolation, lease recovery, retry, duplicate, and out-of-order coverage. The prescribed `scripts/test-all.ps1` passed its sensitive-data scan, Release build, backend tests, web 17-test suite, typecheck, lint, and Playwright 1-test browser smoke. A fresh clone of commit `15fa771` applied all migrations through `20260829234957_Phase7SimulatedDispatch` and seeded 3 fictional users and 12 fictional practitioners against an isolated PostgreSQL 18.4 container. The Phase 7 review gate remains open for project-owner review and any separately authorized tag/push; no push or tag has been made.
