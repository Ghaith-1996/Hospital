# Task 6 Report: Alerts Overview, Tabs, Filters, Table, Mobile Cards

Date: 2026-09-02
Worktree: `D:\hospital\Hospital\.worktrees\frontend-prototype-redesign`
Task scope: Implement only Task 6 for the frontend-only fictional alerts overview.

## RED evidence

Command:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

Initial result:

```text
FAIL  tests/alerts-overview.test.tsx
Error: Failed to resolve import "../app/alerts/page" from "tests/alerts-overview.test.tsx". Does the file exist?
```

Interpretation:

- The new overview test failed before production code existed.
- Failure reason matched the brief: `/alerts` had not been implemented yet.

## GREEN evidence

Focused overview test after implementation:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

Result:

```text
✓ tests/alerts-overview.test.tsx (1 test)
Test Files  1 passed (1)
Tests  1 passed (1)
```

Required focused checks:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx tests/app-shell.test.tsx
npm --prefix src/web run typecheck
npm --prefix src/web run lint
```

Results:

```text
✓ tests/alerts-overview.test.tsx (1 test)
✓ tests/app-shell.test.tsx (9 tests)
Test Files  2 passed (2)
Tests  10 passed (10)
```

```text
> tsc --noEmit
```

```text
Lint did not pass cleanly.

Pre-existing shared-component findings remained:
- src/web/components/layout/app-shell.tsx:54 react-hooks/set-state-in-effect
- src/web/components/ui/tabs.tsx:24 react-hooks/set-state-in-effect
```

Interpretation:

- Task 6 tests passed.
- Existing shell tests still passed.
- TypeScript passed.
- Lint still reports only the already-known shared findings outside Task 6 scope.

## Files changed

- `src/web/app/alerts/page.tsx`
- `src/web/components/alerts/alert-list.tsx`
- `src/web/app/globals.css`
- `src/web/tests/alerts-overview.test.tsx`

## Implementation decisions

- Used the canonical `selectAlerts`, `AlertFilters`, `AlertRecord`, `StatusBadge`, `Tabs`, and `Link` interfaces from prior tasks.
- Kept the overview frontend-only and local; no network or `/api` behavior was added.
- Implemented a semantic desktop table with caption `Fictional alerts`, column headers, and explicit row links instead of row click handlers.
- Implemented mobile cards from the same alert records with the same five displayed fields: Patient Reference, Urgency, Status, Recipients, and Last Updated.
- Kept recipient counts as `responded/total`, where responded means any recipient response other than `none`.
- Kept status separate from response and delivery state.
- Treated the `In Progress` tab as including both `in-progress` and seeded `escalating` alerts so all five seeded alerts remain reachable without inventing a seventh tab that the brief did not authorize.
- Kept tab labels exact: All, Draft, Sent, In Progress, Resolved, Cancelled.
- Used an accessible filter button label that announces the active filter count.
- Implemented local page state for `statusTab`, `draftFilters`, `appliedFilters`, and `filtersOpen`.

## Commands and outputs

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

```text
RED: import failed because ../app/alerts/page did not exist.
GREEN: 1 test passed.
```

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx tests/app-shell.test.tsx
```

```text
2 test files passed, 10 tests passed.
```

```powershell
npm --prefix src/web run typecheck
```

```text
Passed with exit code 0.
```

```powershell
npm --prefix src/web run lint
```

```text
Failed only on the known existing react-hooks/set-state-in-effect errors in app-shell.tsx and tabs.tsx.
```

```powershell
git diff --check
```

```text
No diff-format errors. Git printed a line-ending warning for globals.css in the working copy.
```

## Self-review

- Confirmed the new test was written and observed failing before overview production code existed.
- Confirmed the overview route uses only local prototype state and selectors.
- Confirmed table rows are not clickable; navigation uses explicit links.
- Confirmed accessible caption, table headers, and open-link naming.
- Confirmed cards are rendered from the same source list and expose the same five fields.
- Confirmed the 640px/641px responsive split is implemented in CSS.
- Confirmed no unrelated shared-component fixes were folded into Task 6.

## Limitations

- The repository’s full web lint command still fails on two pre-existing shared files outside Task 6 scope, so Task 6 cannot honestly claim a fully clean lint run.
- The seeded data set still contains no `cancelled` alert row; the Cancelled tab is implemented and currently shows an empty state/count of zero based on existing seed data.

## Concerns

- If the project owner wants `escalating` excluded from `In Progress`, the current six-tab design would need an explicit product decision about where that seeded record belongs.
- The working tree reports a line-ending warning for `src/web/app/globals.css`; it is non-blocking but worth normalizing repository-wide if consistent LF policy matters.

---

## Fix Round 1 - 2026-09-02

Reviewer findings addressed:

- Medium: empty-state copy did not distinguish an empty selected status tab from an empty overall dataset.
- Low: the overview test suite did not cover a zero-result tab such as `Cancelled`.

### RED evidence

Command:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

Result before the page fix:

```text
FAIL  tests/alerts-overview.test.tsx > shows tab-specific empty copy when the selected status tab has no alerts
Unable to find an element with the text: No cancelled alerts yet.
Rendered copy was:
- No alerts are available.
- This local overview will show fictional alerts once they are created.
```

Interpretation:

- The new regression test failed for the expected reason.
- The `Cancelled` tab incorrectly reused the empty-overall copy even though seeded alerts exist in other tabs.

### GREEN evidence

Focused regression test after the page fix:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx
```

```text
✓ tests/alerts-overview.test.tsx (2 tests)
Test Files  1 passed (1)
Tests  2 passed (2)
```

Covering checks:

```powershell
npm --prefix src/web test -- --run tests/alerts-overview.test.tsx tests/app-shell.test.tsx
npm --prefix src/web run typecheck
npm --prefix src/web run lint
```

```text
✓ tests/alerts-overview.test.tsx (2 tests)
✓ tests/app-shell.test.tsx (9 tests)
Test Files  2 passed (2)
Tests  11 passed (11)
```

```text
> tsc --noEmit
```

```text
Lint still fails only on the known shared findings:
- src/web/components/layout/app-shell.tsx:54 react-hooks/set-state-in-effect
- src/web/components/ui/tabs.tsx:24 react-hooks/set-state-in-effect
```

### Files changed in fix round 1

- `src/web/app/alerts/page.tsx`
- `src/web/tests/alerts-overview.test.tsx`

### Fix details

- Added a dedicated `Cancelled`-tab regression test.
- Updated the overview empty-state logic to distinguish:
  - empty selected status tab;
  - empty filtered result; and
  - empty overall dataset.
- Preserved the exact six tabs and the existing five seeded rows.
- Did not add network, API, timer, or unrelated shared-component changes.

### Concerns after fix round 1

- Lint remains blocked by the same known shared `app-shell.tsx` and `tabs.tsx` findings outside this narrow Task 6 fix scope.
