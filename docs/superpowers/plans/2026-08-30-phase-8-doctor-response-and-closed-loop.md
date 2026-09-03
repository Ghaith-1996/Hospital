# Phase 8 Doctor Response and Closed Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Development/Test-only fictional practitioner inbox, idempotent response commands, responsibility assignment, and a safe operator live projection.

**Architecture:** Resolve practitioner identity through an explicit organization-scoped link, persist practitioner-level response semantics transactionally, and expose separate practitioner and operator query surfaces. Reuse the Phase 7 delivery model without adding providers, callbacks, escalation, or resolution policy.

**Tech Stack:** C# 14, .NET 10, ASP.NET Core minimal APIs, EF Core 10, Npgsql/PostgreSQL 18, xUnit, FluentAssertions, Testcontainers, Next.js 16, React 19, TypeScript, Vitest, Testing Library, Playwright.

**Spec:** `docs/superpowers/specs/2026-08-30-phase-8-doctor-response-and-closed-loop-design.md`

## Global Constraints

- Simulation-only and fail-closed outside `Development`/`Test`.
- Server derives organization, user, roles, and practitioner; no caller identity fields.
- Opened, acknowledged, accepted, delivery, and alert lifecycle remain separate.
- UTC, idempotency, transactionality, organization scoping, safe RFC 7807 errors, and sanitized audit are mandatory.
- No real provider, callback, escalation, resolve/cancel/transfer, hospital integration, production identity, AI, or Phase 9 work.

---

### Task 1: Phase and design baseline

**Files:** `AGENTS.md`, `README.md`, `docs/product/definition-of-done.md`, this spec, and this plan.

- [x] Record the owner-approved Phase 8 boundary and the pushed Phase 7 commit.
- [x] Verify the isolated worktree baseline build and tests.

### Task 2: Practitioner identity and response domain

**Files:**
- Create: `src/backend/CriticalAlerts.Domain/Identity/PractitionerUserLink.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Delivery/DeliveryAttempt.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Delivery/RecipientResponse.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Delivery/ResponsibilityAssignment.cs`
- Modify: `src/backend/CriticalAlerts.Domain/Identifiers.cs`
- Test: `tests/CriticalAlerts.Domain.Tests/RecipientResponseStateTests.cs`

- [x] Write failing tests for explicit organization scope, UTC open timestamps, non-SecureMessage `NotApplicable`, acknowledgement independence, accepted assignment, and safe reason codes.
- [x] Implement the smallest domain APIs that pass.
- [x] Run the focused domain tests and the full domain project.

### Task 3: PostgreSQL mapping and migration

**Files:**
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/CriticalAlertsDbContext.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/IdentityConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/Configurations/DeliveryConfigurations.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Persistence/DemoDataSeeder.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Persistence/Migrations/*_Phase8PractitionerResponses.cs`
- Test: `tests/CriticalAlerts.Infrastructure.Tests/RecipientResponsePersistenceTests.cs`

- [x] Write failing PostgreSQL tests for link uniqueness, multi-channel one-response constraints, terminal conflicts, accepted assignment uniqueness, and additive seed behavior.
- [x] Add mappings, partial unique indexes, composite foreign keys, and explicit Riley link.
- [x] Generate and inspect the migration; run focused infrastructure tests.

### Task 4: Response application services and API

**Files:**
- Create: `src/backend/CriticalAlerts.Application/Responses/RecipientResponseContracts.cs`
- Create: `src/backend/CriticalAlerts.Application/Responses/SimulationResponseEnvironmentGuard.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Responses/PractitionerIdentityResolver.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientInboxService.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Responses/RecipientResponseService.cs`
- Create: `src/backend/CriticalAlerts.Api/Http/RecipientResponseEndpoints.cs`
- Modify: API authorization registration, service registration, and `Program.cs`.
- Test: `tests/CriticalAlerts.Api.IntegrationTests/RecipientResponseAuthorizationTests.cs`

- [x] Write failing API tests for 401/403/404 boundaries, exact mapped recipient access, cross-organization isolation, stale version, forged identity fields, open semantics, response idempotency/races, transaction rollback, and safe output/logs.
- [x] Implement Practitioner-only inbox/detail/open/response endpoints with server-derived identity.
- [x] Run focused API tests and full backend regression tests.

### Task 5: Operator live projection

**Files:**
- Create: `src/backend/CriticalAlerts.Application/Responses/AlertLiveContracts.cs`
- Create: `src/backend/CriticalAlerts.Infrastructure/Responses/AlertLiveQueryService.cs`
- Create: `src/backend/CriticalAlerts.Api/Http/AlertLiveEndpoints.cs`
- Test: `tests/CriticalAlerts.Api.IntegrationTests/AlertLiveAuthorizationTests.cs`

- [x] Write failing tests for Operator/Administrator access, Practitioner/anonymous denial, organization isolation, multi-channel status, response distinctions, safe failures, and protected-value exclusion.
- [x] Implement the read-only organization-scoped projection.
- [x] Run focused API tests.

### Task 6: Practitioner and operator web experiences

**Files:**
- Modify: `src/web/lib/alerts.ts`
- Create: `src/web/app/my-alerts/page.tsx`
- Create: `src/web/app/my-alerts/[id]/page.tsx`
- Create: `src/web/app/alerts/[id]/live/page.tsx`
- Create: focused files under `src/web/tests/`
- Modify: `tests/e2e/platform-smoke.spec.ts`

- [x] Write failing component tests for recipient scoping/error states, deliberate response actions, double-submit protection, explicit state semantics, accessible status labels, and polling cleanup.
- [x] Implement the pages and typed API client.
- [x] Add route-driven Playwright coverage and run web unit/typecheck/lint/build/e2e checks.

### Task 7: Documentation and complete verification

**Files:** relevant workflow, state-machine, data-model, security, logging, README, AGENTS, and definition-of-done documents.

- [x] Document the final simulation behavior and every `REQUIRES_HOSPITAL_DECISION` boundary.
- [x] Run sensitive-data and scope scans plus `git diff --check`.
- [x] Run pinned restore, Release build, complete backend tests, web checks, Playwright, and `scripts/test-all.ps1`.
- [x] Verify a clean clone can migrate through Phase 8, apply additive demo seed, and pass focused Phase 8 tests against PostgreSQL 18.4.
- [x] Review the complete diff and report results; separate push authorization was obtained from the project owner. No tag was requested.
