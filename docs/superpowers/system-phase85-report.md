# Phase 8.5 System Harness Report

Status: final system harness executed locally on 2026-09-05 against the connected frontend corrections. Hosted GitHub Actions has not run for this local commit.

## Harness boundary

`scripts/system-e2e.ps1` owns one uniquely named PostgreSQL 18.4 container and the API, simulation worker, and production Next.js processes. Each run generates an ephemeral PostgreSQL password, username, run identifier, ports, and 32-byte data-protection key. None are written to the repository or printed. The database uses the backend-approved `critical_alerts_test_*` local naming guard. The script runs every migration and invokes `database reset-demo --confirm-demo-reset` explicitly before starting the application stack.

The API runs in Test with development authentication and simulation responses enabled. The worker runs in Test with simulation dispatch enabled. No provider or external network adapter is configured. Next.js is built with the run's loopback API target, then served in production mode. Chromium sends requests through the real Next.js `/api/v1/*` rewrite.

The API, worker and Next server launch as their direct Release DLL/Node processes so POSIX cleanup cannot orphan CLI children. The `finally` block terminates owned process trees, force-removes the uniquely named database container, checks for a remaining container, checks each tracked process, and probes the three owned ports. Any cleanup residue fails the command.

## Executed results

- `pwsh -NoProfile -File scripts/system-e2e.ps1`: 3 Playwright tests passed in 15.9 seconds. The same command also applied all migrations, explicitly reset fictional demo data, built the Release worker with 0 warnings and 0 errors, and completed a production Next.js build with 15 routes. Teardown proof: `container_remaining=0 live_owned_processes=0 ports_closed=1`.
- Scenario A passed through Chromium: backend development sign-in as Jordan Lee; browser creation with human-entered SBAR and `heartRate` 118 `beats/min`; deliberate secure-message approval; manual selection of Maya Chen and server-linked Riley Sato (`SIM-PRAC-0108`) with two visible SecureMessage channels; recipient-edit critical-field reconfirmation; exact server review; double-click-safe confirmation; `DispatchQueued`; real worker delivery; Riley open, acknowledge, and accept responsibility as separate states; Jordan live view, resolve, and reload.
- Scenario B passed: the Alerts hub opened compose through an App Router transition at version N. A same-document Back traversal raised a `confirm` dialog; dismissing it retained the route and local source edit. A real API update then created N+1. The stale browser save received the backend concurrency conflict and retained the local buffer. Only the explicit `Discard local edits and load server version` action loaded the concurrent server source and N+1 with confirmation review required again. A second edit and accepted `confirm` Back traversal returned to `/alerts`.
- Scenario C passed against the real backend: concurrent same-key confirmation returned one normal success and one explicit replay success. PostgreSQL assertions observed exactly 1 matching `outbox_messages` row, 1 selected logical recipient, and 1 resulting `delivery_attempts` row.
- `npm run web:e2e` with the repository-local Chromium cache: 1 standalone smoke test passed in 3.4 seconds. It proved simulation labeling and a fail-closed UI when backend development authentication is unavailable.
- `pwsh -NoProfile -File scripts/verify-web-storage-safety.ps1`: passed with zero active-route localStorage/sessionStorage/prototype-store violations.
- Playwright discovery: 3 system tests in 1 file and 1 smoke test in 1 file.
- PowerShell parser and `git diff --check`: passed.
- Six fictional-only screenshots were captured outside the repository under `C:\Users\ghait\AppData\Local\Temp\critical-alerts-system-43cd3fc6b1104686bee5ffcc1da7e99e\screenshots`: new, compose, exact review, live delivery, practitioner alert, and a 390×844 resolved mobile shell.

## CI changes

The existing single `verify` job and every prior gate remain. New steps enforce active-workflow storage safety, a production web build, and the connected system E2E after Chromium installation. `scripts/test-all.ps1` runs the same added checks locally. OpenAPI verification continues through the current generated-contract script.

Hosted CI status remains unverified until the branch is pushed and GitHub Actions completes. This report does not claim the whole Phase 8.5 gate is complete.
