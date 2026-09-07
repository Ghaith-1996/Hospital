# Phase 8.5 Backend Contract Slice Report

Date: 2026-09-05

## Scope and decisions

- Added `GET /api/v1/dev/location-context` only when development authentication is enabled. The existing environment guard limits that configuration to Development and Test.
- The endpoint requires the existing `AlertDraftEditor` policy and derives its organization solely from the authenticated claim.
- The response contains only an organization identifier and nested site/department identifiers and names. It exposes no simulation codes, contacts, practitioner data, patient data, or policy.
- Sites and departments are ordered by name and identifier. Database predicates constrain both queries to the authenticated organization.
- Replaced the hand-maintained partial OpenAPI file with deterministic output from the executable API host in Test configuration. Generation uses a random in-memory data-protection key and a deliberately unused database connection; requesting OpenAPI does not access the database.
- Semantic comparison sorts JSON object properties and schema arrays whose ordering does not affect meaning (`required`, `enum`, `type`, `allOf`, `anyOf`, `oneOf`, and `tags`). Other arrays retain their order.
- The runtime contract omits its environment-specific server URL and supplies stable simulation title/description metadata.
- Inspected the alert aggregate, draft/review services, directory/dispatch/response contracts, API endpoints/Program, worker, and representative API tests. No safety correction was made because this slice exposed no failing safety behavior.

## TDD evidence

The first focused location run failed for the intended missing-route reason: three failures returned 404 rather than the expected 401, 403, and scoped 200 result; the two disabled-environment 404 cases already passed. After the endpoint was implemented, all five passed.

The first complete contract comparison failed because the committed document was a partial hand-maintained contract. A later determinism verification detected the generated host URL changing between runs. Stable document metadata removed that environment-specific value. A separate red test proved the new operation initially lacked declared response schema and authorization statuses; adding endpoint metadata made it pass.

## Verification results

- `./.dotnet10/dotnet.exe test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~SimulationLocationContextTests --verbosity minimal`: 5 passed, 0 failed.
- `./scripts/verify-openapi.ps1 -WriteDocument`: generated the deterministic OpenAPI 3.1 document from a real API process.
- `./scripts/verify-openapi.ps1`: complete runtime/committed semantic comparison passed.
- `./.dotnet10/dotnet.exe test tests/CriticalAlerts.Api.IntegrationTests/CriticalAlerts.Api.IntegrationTests.csproj --no-restore --verbosity minimal`: 106 passed, 0 failed, 0 skipped; disposable PostgreSQL 18 containers were used.
- `./.dotnet10/dotnet.exe format src/backend/CriticalAlerts.sln --no-restore --verify-no-changes --verbosity minimal`: passed after normalizing line endings.
- `git diff --check`: passed before final line-ending normalization; final diff inspection is required with the parent integration work because frontend files are being edited concurrently in the shared worktree.

## Limitations and sequencing

- This slice verifies the exact metadata emitted by the executable host. Most pre-existing minimal API handlers return `IResult` without explicit `Produces` metadata, so their generated success response schemas and alternate runtime statuses remain sparse. The new location endpoint explicitly declares 200, 401, and 403 schemas/statuses. Broadly annotating every historical endpoint was not attempted without endpoint-by-endpoint failing tests.
- OpenAPI generation and semantic verification require the pinned .NET SDK but no database or Docker. API behavior tests require Docker for disposable PostgreSQL 18.
- No frontend, CI, real provider, AI, production policy, real data, repository setting, or Phase 9 behavior was added.
- Production location context and production authentication/directory mapping remain `REQUIRES_HOSPITAL_DECISION`.

Proposed focused commit message: `feat(api): generate contract and expose simulation locations`
