# Phase 4 Directory Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden and verify the existing fictional directory integration boundary so Phase 4 can be reviewed without adding Phase 5 or any real hospital integration.

**Architecture:** Keep `IDirectorySourceAdapter` as the source-specific boundary and keep CSV normalization in the Application project. Keep reconciliation, PostgreSQL persistence, sync-run accounting, and sanitized audit metadata in Infrastructure. Keep authorization and organization context server-derived in the API; the web UI only presents state and submits a CSV file.

**Tech Stack:** C# 14, .NET 10, ASP.NET Core, EF Core/Npgsql, PostgreSQL/Testcontainers, xUnit/FluentAssertions, TypeScript, React, Next.js App Router, Vitest/Testing Library, Playwright, PowerShell.

**Spec:** `docs/superpowers/specs/2026-08-25-phase-4-directory-hardening-design.md`

## Global Constraints

- Implement only the Phase 4 scope in `AGENTS.md`: fictional directory integration boundary, CSV adapter, validation/normalization/duplicate detection, practitioner search, on-call/freshness display, and admin import UI.
- Use fictional `SIM-` identifiers and `555` phone values only; do not add real names, employee data, contact values, schedules, credentials, patient data, or sensitive screenshots.
- Do not add alert drafting, recipient dispatch, provider adapters, outbox work, AI, speech, Entra, SCIM, Graph, FHIR, scheduling, connector, or production identity.
- Preserve organization scoping and server-derived authenticated identity; client headers, query values, and form fields are never trusted for user, organization, or role.
- Preserve `REQUIRES_HOSPITAL_DECISION` for production freshness thresholds, stale-selection behavior, deactivation timing, merge policy, and source-of-truth decisions.
- Follow red-green-refactor for every production behavior change and run fresh verification before any completion or tag claim.

---

### Task 1: Strict CSV structure and synthetic endpoint validation

**Files:**
- Modify: `src/backend/CriticalAlerts.Application/Directory/CsvDirectoryParser.cs`
- Test: `tests/CriticalAlerts.Application.Tests/CsvDirectoryParserTests.cs`
- Test: `tests/CriticalAlerts.Architecture.Tests/RepositorySafetyTests.cs` only if a new parser safety invariant needs repository-level coverage

**Interfaces:**
- Consumes: `CsvDirectorySourceAdapter.Read(Stream)` and the existing `CsvDirectoryParser.Parse` overloads.
- Produces: the existing `DirectoryParseResult`/`DirectoryImportIssue` contract with deterministic issue codes and row numbers; no new source-specific persistence contract.

- [ ] **Step 1: Write failing parser tests for duplicate headers and row shape**

Add focused tests to `CsvDirectoryParserTests`:

```csharp
[Fact]
public void DuplicateHeadersAreRejected()
{
    var csv = "source_record_id,first_name,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status\n";

    var parsed = CsvDirectoryParser.Parse(csv);

    parsed.Practitioners.Should().BeEmpty();
    parsed.Errors.Should().Contain(error => error.Code == "duplicate-header");
}

[Fact]
public void RowsWithUnexpectedColumnCountsAreRejected()
{
    var csv = "source_record_id,first_name,last_name,simulation_code,specialty,site_code,department_code,role_title,is_primary_role,is_active,source_updated_at_utc,freshness_status\n"
        + "SIM-SRC-MAYA,Maya,Chen,SIM-PRAC-0101,Emergency,SIM-SITE-NORTH,SIM-DEPT-EMERGENCY,Emergency physician,true,true,2026-08-01T12:00:00Z,current,unexpected\n";

    var parsed = CsvDirectoryParser.Parse(csv);

    parsed.Practitioners.Should().BeEmpty();
    parsed.Errors.Should().Contain(error => error.Code == "invalid-column-count");
}
```

- [ ] **Step 2: Run the targeted parser tests and confirm the expected red failures**

Run:

```powershell
dotnet test tests/CriticalAlerts.Application.Tests/CriticalAlerts.Application.Tests.csproj --filter FullyQualifiedName~CsvDirectoryParserTests --nologo
```

Expected with the current implementation: the command reaches the test project and the two new tests fail because duplicate headers and extra columns are currently accepted. If the pinned SDK is still unavailable, record the exact SDK-resolution failure and do not claim a red test result.

- [ ] **Step 3: Write failing tests for malformed quotes and endpoint scheme/patterns**

Add these behaviors and a `ValidSingleRow` helper that uses the same synthetic values as the fixture:

```csharp
private static string ValidSingleRow(string endpointKind, string endpointValue)
    => string.Join(",",
    [
        "SIM-SRC-ONE", "Maya", "Chen", "SIM-PRAC-0101", "Emergency", "SIM-SITE-NORTH",
        "SIM-DEPT-EMERGENCY", "Emergency physician", "true", "true", endpointKind, endpointValue,
        "SIM-ENDPOINT-0101", "2026-08-01T12:00:00Z", "current",
    ]) + Environment.NewLine;

[Fact]
public void UnterminatedQuotedFieldIsRejected()
{
    var parsed = CsvDirectoryParser.Parse("source_record_id,first_name\nSIM-SRC-ONE,\"Maya\n");

    parsed.Practitioners.Should().BeEmpty();
    parsed.Errors.Should().Contain(error => error.Code == "malformed-csv");
}

[Fact]
public void SecureMessageEndpointsRequireTheSimulationScheme()
{
    var parsed = CsvDirectoryParser.Parse(ValidSingleRow(endpointKind: "SecureMessage", endpointValue: "https://example.invalid/endpoint"));

    parsed.Errors.Should().Contain(error => error.Code == "non-synthetic-endpoint");
}

[Fact]
public void PhoneValidationRequiresACompleteSynthetic555Number()
{
    var parsed = CsvDirectoryParser.Parse(ValidSingleRow(endpointKind: "Sms", endpointValue: "+1 555-010-0101-extra"));

    parsed.Errors.Should().Contain(error => error.Code == "non-synthetic-endpoint");
}
```

The test helper must construct only fictional `SIM-` values and the `555` address used by the fixture.

- [ ] **Step 4: Run the new targeted tests and verify they fail for the missing behaviors**

Run the same `dotnet test` command from Step 2. Confirm each failure is caused by the parser behavior, not by a test setup error; otherwise correct the test before implementation.

- [ ] **Step 5: Implement the minimal parser hardening**

Update `CsvDirectoryParser` to:

1. preserve the existing required-header list and reject a repeated normalized header with `duplicate-header`;
2. parse each physical row with a `TryParseLine`-style result so an unterminated quoted field yields `malformed-csv` with its physical row number;
3. reject any nonblank row whose field count differs from the header count with `invalid-column-count`;
4. validate SMS/voice values with the complete synthetic `555` number shape already permitted by repository safety rules rather than `string.Contains("555")`;
5. require `sim-secure://` for secure-message references; and
6. keep existing UTC, `SIM-` prefix, enum, duplicate-code, and cross-row consistency rules unchanged.

Do not echo endpoint values in error messages or introduce a production freshness rule.

- [ ] **Step 6: Run the targeted parser tests and refactor only while green**

Run:

```powershell
dotnet test tests/CriticalAlerts.Application.Tests/CriticalAlerts.Application.Tests.csproj --filter FullyQualifiedName~CsvDirectoryParserTests --nologo
```

Expected: all parser tests pass with zero failures. Keep parser helpers small and retain row-numbered issue reporting.

### Task 2: Reconciliation, sync-run, organization, and API regression coverage

**Files:**
- Modify: `tests/CriticalAlerts.Infrastructure.Tests/DirectoryImportAndSearchTests.cs`
- Modify: `tests/CriticalAlerts.Api.IntegrationTests/DirectoryAuthorizationAndImportTests.cs`
- Modify: `src/backend/CriticalAlerts.Infrastructure/Directory/DirectoryImportService.cs` only if a new regression test demonstrates a behavior gap
- Modify: `src/backend/CriticalAlerts.Api/Http/DirectoryEndpoints.cs` only if a new regression test demonstrates an API boundary gap

**Interfaces:**
- Consumes: `IDirectoryImportService`, `IDirectorySearchService`, `DirectoryImportPreviewResult`, `DirectoryImportApplyResult`, authenticated seeded identities, and PostgreSQL fixtures.
- Produces: no new public integration surface unless an existing Phase 4 requirement cannot be proved through the current endpoints.

- [ ] **Step 1: Add failing infrastructure tests for repeat apply and sync accounting**

Extend the existing PostgreSQL integration coverage with a test that applies the same Harborview CSV twice after a reset and asserts:

```csharp
(await db.DirectorySourceRecords.CountAsync(record => record.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(12);
(await db.DirectorySyncRuns.CountAsync(run => run.SourceSystem == DirectorySourceSystems.Csv && run.Status == DirectorySyncRunStatus.Succeeded)).Should().Be(2);
(await db.ContactEndpoints.CountAsync(endpoint => endpoint.OrganizationId == DemoDataSeeder.OrganizationId)).Should().Be(11);
(await db.OnCallAssignments.CountAsync(assignment => assignment.SourceSystem == DirectorySourceSystems.Csv)).Should().Be(2);
```

Also add a blocking-conflict apply test that submits an unknown department and proves `Applied` is false, `SyncRunId` is null, and no CSV source record or sync run is written.

- [ ] **Step 2: Run the targeted infrastructure tests and verify the expected red result**

Run:

```powershell
dotnet test tests/CriticalAlerts.Infrastructure.Tests/CriticalAlerts.Infrastructure.Tests.csproj --filter FullyQualifiedName~DirectoryImportAndSearchTests --nologo
```

Expected: the repeat/conflict assertions identify any actual reconciliation gap. If the current implementation already satisfies them, the new tests may pass immediately; record that as regression coverage and do not invent a production change.

- [ ] **Step 3: Add failing API boundary tests**

Extend `DirectoryAuthorizationAndImportTests` with tests that:

1. submit a caller-supplied `X-Organization-ID` value while signed in as a seeded Administrator and prove the returned directory remains the authenticated organization;
2. submit an empty multipart form and receive `400` with `csv-file-required`;
3. submit a syntactically valid but blocking CSV as Administrator and receive a non-applied result with no persisted sync run; and
4. assert that unauthenticated search/import and Practitioner import remain `401`/`403` with safe problem details that do not contain role or user enumeration.

Use only existing seeded handles and fictional payloads. Do not add an organization ID field to the request model.

- [ ] **Step 4: Run the targeted API tests and inspect failures**

Run:

```powershell
dotnet test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj --filter FullyQualifiedName~DirectoryAuthorizationAndImportTests --nologo
```

Expected: failures, if any, identify a server-side boundary issue. A test that passes against the current implementation becomes documented negative authorization coverage.

- [ ] **Step 5: Implement only proven backend gaps and rerun targeted suites**

Preserve the current server-derived claims flow. If a test fails, make the smallest change that keeps organization scoping in `TryGetActor`, keeps authorization policies on the endpoint group, and keeps preview conflict handling before the transaction. Rerun both targeted commands from Steps 2 and 4; expected result is zero failures.

### Task 3: Directory search and import UI safety behavior

**Files:**
- Modify: `src/web/app/directory/page.tsx`
- Modify: `src/web/app/directory/import/page.tsx`
- Modify: `src/web/tests/page.test.tsx`

**Interfaces:**
- Consumes: current JSON DTOs from `/api/directory/practitioners` and `/api/directory/imports/preview`/`/api/directory/imports`.
- Produces: a UI that only reflects server authorization, renders freshness/on-call synchronization metadata, and invalidates a preview when the selected file changes.

- [ ] **Step 1: Add failing web tests for on-call timestamp and stale preview invalidation**

Add a directory fixture containing an inactive/stale practitioner with an on-call source and timestamp. Render `DirectoryPage` with a mocked successful fetch and assert the rendered row contains the source and normalized timestamp and shows `Inactive / Stale` plus `No` for Selectable.

Add an import-page test that uses Testing Library `fireEvent.change` with two synthetic `File` objects, mocks a clean preview for the first upload, then uploads the second file and asserts the preview disappears and Apply is disabled:

```tsx
const input = screen.getByLabelText("Simulation CSV");
const apply = screen.getByRole("button", { name: "Apply import" });
expect(apply).toBeDisabled();
fireEvent.change(input, { target: { files: [new File(["first"], "first.csv", { type: "text/csv" })] } });
fireEvent.click(screen.getByRole("button", { name: "Preview import" }));
expect(await screen.findByText(/Preview ready for SIM-CSV/)).toBeVisible();
expect(apply).toBeEnabled();
fireEvent.change(input, { target: { files: [new File(["second"], "second.csv", { type: "text/csv" })] } });
expect(screen.queryByText(/Preview ready for SIM-CSV/)).not.toBeInTheDocument();
expect(apply).toBeDisabled();
```

The test must verify that changing the selected file clears the previous preview rather than allowing an apply request for a different file.

- [ ] **Step 2: Run the targeted web tests and confirm the expected red result**

Run:

```powershell
npm --prefix src/web test -- --run src/web/tests/page.test.tsx
```

Expected with the current implementation: the on-call timestamp assertion and the changed-file preview assertion fail. If dependencies are unavailable, record the exact missing-command result.

- [ ] **Step 3: Implement the minimal UI fixes**

In `directory/page.tsx`, render `onCallLastSynchronizedAtUtc` beside the on-call source using the existing ISO formatting pattern.

In `directory/import/page.tsx`, add a file-change handler that updates the file, clears `preview`, and resets the status to require a new preview. Keep the Apply button disabled unless a clean preview belongs to the currently selected file. Do not treat this client state as authorization; the API remains authoritative.

- [ ] **Step 4: Run targeted web tests, typecheck, and lint**

Run:

```powershell
npm --prefix src/web test -- --run src/web/tests/page.test.tsx
npm --prefix src/web run typecheck
npm --prefix src/web run lint
```

Expected: zero test failures, zero TypeScript errors, and zero lint errors in the pinned Node/npm environment.

### Task 4: Documentation, full verification, and Phase 4 review package

**Files:**
- Modify: `docs/architecture/directory-integration.md`
- Modify: `docs/product/definition-of-done.md`
- Modify: `README.md` only after all required checks pass and the phase status is ready for human review
- Test/verify: `scripts/verify-no-sensitive-data.ps1`, `scripts/test-all.ps1`, fresh-clone check

**Interfaces:**
- Consumes: completed backend/web behavior and fresh verification output.
- Produces: an accurate Phase 4 review package; no phase tag until the human review gate is satisfied.

- [ ] **Step 1: Update directory documentation with the strict simulation contract**

Document the duplicate-header, row-width, malformed-quote, synthetic endpoint, source-record/simulation-code matching, preview non-mutation, and UI preview invalidation behavior. Keep all production policy decisions marked `REQUIRES_HOSPITAL_DECISION`.

- [ ] **Step 2: Run the repository safety check and inspect the diff**

Run:

```powershell
./scripts/verify-no-sensitive-data.ps1
git diff --check
git status --short
```

Expected: the safety script passes, the diff has no whitespace errors, and every changed file is within the approved Phase 4 surface.

- [ ] **Step 3: Run the complete prescribed verification**

Run:

```powershell
./scripts/test-all.ps1
```

Expected in the pinned environment: backend build/tests, web tests, typecheck, lint, and Playwright all pass. In the current environment the command is expected to stop at missing .NET SDK `10.0.100`; do not change pins or claim completion if that remains true. Record the exact command output and also record any web dependency/toolchain result separately.

- [ ] **Step 4: Perform a fresh-clone verification when the pinned toolchain is available**

From a clean clone of the reviewed commit, run the repository's documented setup and verification commands without using generated outputs from the working checkout. Confirm the fixture, migrations, tests, and web shell work from that clone. If network/toolchain access prevents this, state the precise human action required.

- [ ] **Step 5: Prepare the Phase 4 review report and stop before tagging**

Report files changed, decisions made, commands run, test results, limitations, human actions required, and proposed commit message. Do not create or push `phase-4` until the human reviewer approves the verified implementation and the pinned full suite has passed.
