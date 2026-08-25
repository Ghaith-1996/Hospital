# Phase 4 Directory Boundary Hardening Design

## Status

Approved in chat on 2026-08-25 for implementation planning. This document narrows the work to the active Phase 4 scope in `AGENTS.md`; it does not authorize Phase 5 or any hospital integration.

## Current baseline

Commit `9c2e226` already contains a substantial Phase 4 implementation even though its commit title is `Phase 3`. The baseline includes:

- fictional practitioner, role, contact endpoint, on-call, source-record, and sync-run persistence;
- the `IDirectorySourceAdapter` application boundary and fictional CSV adapter;
- CSV parsing, normalization, validation, duplicate detection, and payload hashing;
- preview/apply reconciliation with source-record-first and simulation-code matching;
- organization-scoped search and directory import endpoints;
- seeded fictional directory data and the Harborview CSV fixture;
- directory search and import pages in the Next.js simulation shell; and
- application, infrastructure, API, architecture, and web tests for the main flow.

The working tree is clean and no `phase-4` tag exists. The implementation has not received a fresh full verification run in this environment: the repository pins .NET SDK `10.0.100`, while the machine currently exposes SDK `9.0.310`; the required Node/npm versions and installed web dependencies are also unavailable.

## Goal

Make the existing fictional directory boundary safe and reviewable for Phase 4 closure by proving strict CSV input handling, deterministic normalization and reconciliation, organization-scoped authorization, preview non-mutation, freshness/on-call visibility, and an admin import workflow without adding any real integration.

## Non-goals

- No alert drafting, recipient dispatch, provider adapter, outbox work, AI, speech, Entra, SCIM, Graph, FHIR, scheduling, connector, or production identity.
- No invented production freshness window, stale-selection override, deactivation policy, merge policy, or source-of-truth decision. Missing hospital decisions remain marked `REQUIRES_HOSPITAL_DECISION`.
- No real names, identifiers, contact values, schedules, credentials, or patient data.
- No change to the pinned toolchain versions merely to make local verification pass.

## Design

### Integration boundary

`IDirectorySourceAdapter.Read(Stream)` remains the only source-specific entry point. The adapter returns normalized practitioners, roles, protected-contact inputs, on-call assignments, source metadata, blocking errors, and non-blocking warnings. `DirectoryImportService` owns planning, organization-scoped matching, persistence, sync-run accounting, and sanitized audit metadata. Future SCIM, Graph, FHIR, scheduling, or restricted SQL adapters must target this normalized contract rather than the CSV shape.

### Strict fictional CSV contract

The CSV adapter will reject malformed structure instead of silently interpreting it:

- required headers must be present exactly once after case-insensitive trimming;
- quoted fields must be balanced and escaped according to CSV rules;
- required values, booleans, enum values, synthetic identifiers, and location codes must validate with row references;
- timestamps must be UTC instants and retain the normalized UTC representation;
- SMS and voice endpoints must use the fictional `555` pattern, secure-message references must use the `sim-secure://` simulation scheme, and endpoint labels must use synthetic `SIM-` values;
- rows sharing a source record must agree on identity, activity, timestamp, and freshness fields; and
- duplicate simulation codes remain blocking conflicts, while same-name/different-source records remain separate with a warning.

Error and warning responses contain codes, row numbers, and safe synthetic identifiers only; they do not expose protected endpoint values or clinical payloads.

### Reconciliation and persistence

Matching remains organization-scoped and ordered as follows:

1. `(organization, source_system, source_record_id)`;
2. `(organization, simulation_code)` when the source record has not been seen by that adapter.

Display name is never a match key. A source record cannot silently change its simulation code. Preview loads the scoped catalog and produces a plan without tracking or writing entities. Apply rejects any blocking conflict before opening a transaction, then writes practitioner, role, protected endpoint, on-call, and source-record state atomically and records a sanitized sync run/audit event.

### API and UI behavior

The API continues to derive user and organization context from the authenticated server-created principal. Request bodies and headers cannot supply a trusted user, organization, or role. Directory search is available to seeded Operator and Administrator identities; preview/apply is Administrator-only; unauthenticated and unauthorized requests return the existing safe problem details.

The web shell remains visibly simulation-only. Search displays similar-name disambiguation, active/inactive state, source and synchronization time, stale status, role/location, and on-call source/timestamp. The import page previews before apply, displays blocking errors and warnings, disables apply until a clean preview exists, and never treats a client-side control as an authorization boundary.

## Test strategy

Tests will be added or strengthened before production changes:

- application tests for duplicate headers, malformed quotes/row structure, UTC and endpoint validation, and safe error output;
- infrastructure PostgreSQL tests for source-record-first matching, simulation-code conflicts, same-name non-merging, protected endpoint persistence, on-call reconciliation, preview non-mutation, organization isolation, and repeat import behavior;
- API integration tests for unauthenticated, Operator, Administrator, and Practitioner access, missing files, blocking preview/apply behavior, and server-derived organization context;
- web tests for stale/inactive/on-call display, preview/apply state, error rendering, and the absence of dispatch controls; and
- architecture/safety checks confirming no out-of-scope integration or sensitive fixture material is introduced.

The required test cycle is red test first, minimal implementation, green targeted test, then the full relevant suite. Completion requires fresh `scripts/test-all.ps1` evidence; if the pinned SDK/runtime is still unavailable, the result will be reported as blocked rather than represented as a passing Phase 4 gate.

## Closure criteria

Phase 4 may be proposed for review only when the repository demonstrates:

- CSV is an adapter over the shared directory model;
- validation, normalization, duplicate detection, preview, and atomic apply are covered;
- source identifiers and simulation codes—not names—control reconciliation;
- inactive records are non-selectable and stale records are visibly flagged;
- Operator/Administrator search and Administrator-only import are enforced server-side;
- preview does not mutate and apply persists roles, protected endpoints, on-call assignments, and source records;
- no Phase 5 or real integration work was added;
- the complete verification command passes in the pinned environment;
- a fresh-clone check passes; and
- the human reviewer approves the resulting commit before a `phase-4` tag is created and pushed.

## Expected implementation surface

The implementation plan will prefer focused changes in these existing areas:

- `src/backend/CriticalAlerts.Application/Directory/`
- `src/backend/CriticalAlerts.Infrastructure/Directory/`
- `src/backend/CriticalAlerts.Api/Http/DirectoryEndpoints.cs`
- `src/web/app/directory/`
- the corresponding application, infrastructure, API, architecture, and web tests;
- `docs/architecture/directory-integration.md`, `docs/product/definition-of-done.md`, and the Phase 4 status documentation when behavior changes require it.

Database migrations will be changed only if the tests prove the existing Phase 2/Phase 4 schema cannot enforce an already documented boundary. No new production integration table or provider configuration is part of this design.
