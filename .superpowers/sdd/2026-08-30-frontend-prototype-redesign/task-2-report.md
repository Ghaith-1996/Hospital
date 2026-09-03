# Task 2 Report: Typed Fictional Alert Store

## Scope

Implemented only Task 2 in `D:\hospital\Hospital\.worktrees\frontend-prototype-redesign`:

- `src/web/features/alerts/types.ts`
- `src/web/features/alerts/seed.ts`
- `src/web/features/alerts/selectors.ts`
- `src/web/features/alerts/prototype-store.tsx`
- `src/web/tests/prototype-store.test.tsx`
- `src/web/tests/test-utils.tsx`
- `src/web/tests/setup.ts`

## TDD Record

### RED

Command:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Relevant output:

```text
FAIL  tests/prototype-store.test.tsx
Error: Failed to resolve import "@/features/alerts/seed" from "tests/prototype-store.test.tsx". Does the file exist?
Test Files  1 failed (1)
Tests  no tests
```

Result: expected failure because the Task 2 feature modules did not exist yet.

### GREEN

Command:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Relevant output after implementation:

```text
✓ tests/prototype-store.test.tsx (9 tests)
Test Files  1 passed (1)
Tests  9 passed (9)
```

## Verification Commands

Focused test:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Output:

```text
✓ tests/prototype-store.test.tsx (9 tests)
Test Files  1 passed (1)
Tests  9 passed (9)
```

Typecheck:

```powershell
npm --prefix src/web run typecheck
```

Output:

```text
> tsc --noEmit
```

Lint, first run:

```powershell
npm --prefix src/web run lint
```

Relevant output:

```text
error  react-hooks/set-state-in-effect
prototype-store.tsx: setHydrated(true)
prototype-store.tsx: setStorageError(null)
```

Action taken: refactored provider hydration/persistence metadata into a reducer-driven flow, then reran verification.

Lint, final run:

```powershell
npm --prefix src/web run lint
```

Output:

```text
> eslint . --max-warnings=0
```

## Implemented Contract

- Added exact Task 2 unions and record types, plus `PROTOTYPE_SCHEMA_VERSION`, `STORAGE_KEY`, and `DEMO_NOW`.
- Seeded exactly five visible fictional alerts with status variety: `draft`, `sent`, `in-progress`, `resolved`, `escalating`.
- Kept `cancelled` supported in the domain and filters without adding a sixth seed row.
- Assigned three of the five seeded alerts to Dr. Marc Tremblay.
- Included one three-step escalating alert among the five.
- Implemented deterministic reducer, selectors, storage load/save helpers, and a React provider/context API.
- Added focused tests for:
  - draft creation plus confirmation semantics
  - acknowledgement separate from responsibility acceptance
  - incompatible storage reset to deterministic seed
  - selected-user persistence
  - demo reset
  - declined and unavailable responses
  - 500-character note guard
  - storage-write failure preserving in-memory state while surfacing `storageError`
- Updated test cleanup to run both `cleanup()` and `localStorage.clear()`.

## Decisions

- Used deterministic simulation-only timestamps and records; no fetch, API, or backend code was added.
- Derived draft labels from the first sentence of `caseDetails`, trimmed to 64 characters, per brief.
- Used a provider-local reducer wrapper for hydration and storage error tracking so persisted state can be loaded in `useEffect` without violating React lint rules.
- Kept storage compatibility strict: invalid or incompatible stored payloads restore the deterministic seed state.
- Kept the new focused test imports relative instead of expanding repo-wide Vitest alias configuration, to stay within Task 2 scope.

## Test Totals

- Focused Task 2 tests: 9 passed, 0 failed.
- Additional checks: `typecheck` passed, `lint` passed after one provider refactor.

## Limitations

- Verification was scoped to the Task 2 focused test suite plus `typecheck` and `lint`; I did not run the full web test suite.
- The provider restores only storage payloads that match the expected schema version and basic state shape; anything incompatible intentionally resets to deterministic seed data.

## Concerns

- No blocking concerns. The only issue found during verification was the initial effect-based provider state update pattern, which was corrected before commit.

## Fix Round 1

### Reviewer Findings Addressed

- Removed automatic reducer transitions from `declined` or `unavailable` into `escalating`; escalation remains a seeded demo-labelled state only.
- Corrected the resolved seed alert so delivery/receipt/acknowledgement/acceptance are represented separately instead of collapsing them into `not-applicable`.
- Exported `PrototypeAction` as part of the module contract.
- Expanded the focused suite to cover selector behavior and the no-auto-escalation regression.

### Changed Files

- `src/web/features/alerts/prototype-store.tsx`
- `src/web/features/alerts/seed.ts`
- `src/web/tests/prototype-store.test.tsx`

### Fix Round TDD

RED command:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Relevant RED output:

```text
✗ records declined responses without auto-escalating a sent alert
  expected 'escalating' to be 'sent'
✗ records unavailable responses without auto-escalating a sent alert
  expected 'escalating' to be 'sent'
✗ seeds the resolved alert with distinct delivery and acceptance milestones
  expected 'not-applicable' to be 'delivered'
Tests  3 failed | 11 passed (14)
```

GREEN command:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

GREEN output:

```text
✓ tests/prototype-store.test.tsx (14 tests)
Test Files  1 passed (1)
Tests  14 passed (14)
```

### Covering Verification

Focused test:

```powershell
npm --prefix src/web test -- --run tests/prototype-store.test.tsx
```

Output:

```text
✓ tests/prototype-store.test.tsx (14 tests)
Test Files  1 passed (1)
Tests  14 passed (14)
```

Typecheck:

```powershell
npm --prefix src/web run typecheck
```

Output:

```text
> tsc --noEmit
```

Lint:

```powershell
npm --prefix src/web run lint
```

Output:

```text
> eslint . --max-warnings=0
```

### Notes

- Kept exactly five seed alerts and the original mockup status variety.
- Did not add any backend, fetch, or API code.
- Verification remained scoped to the Task 2 focused suite plus `typecheck` and `lint`.
