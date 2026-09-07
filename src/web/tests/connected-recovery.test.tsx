import { act, renderHook } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { useIdempotentAction } from "../features/connected/use-idempotent-action";
import { errorGuidance, useServerQuery } from "../features/connected/common";
import { AlertApiError } from "../lib/alerts";

test.each([
  [401, null, /Session unavailable/],
  [403, null, /not authorized/],
  [404, null, /inaccessible/],
  [409, "stale-alert-version", /reloaded and reviewed/],
  [409, "directory-revision-changed", /review recipients and channels/],
  [429, null, /retry the same action/],
] as const)("maps API %s %s to actionable recovery guidance", (status, code, expected) => {
  expect(errorGuidance(new AlertApiError(status, code, "Safe server message"))).toMatch(expected);
});

test("a successful command with a failed refresh retries the read without repeating the command", async () => {
  const read = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue(undefined);
  const mutate = vi.fn().mockResolvedValue({});
  const { result } = renderHook(() => useIdempotentAction(read));
  await act(() => result.current.execute("Acknowledge", mutate));
  expect(result.current.uncertain).toBeNull();
  expect(result.current.refreshRequired).toBe(true);
  await act(() => result.current.execute("Acknowledge", mutate));
  expect(mutate).toHaveBeenCalledTimes(1);
  await act(() => result.current.refresh());
  expect(read).toHaveBeenCalledTimes(2);
  expect(result.current.refreshRequired).toBe(false);
});

test("uncertain command retry retains its exact key and captured payload", async () => {
  const original = vi.fn().mockRejectedValueOnce(new Error("offline")).mockResolvedValue({});
  const replacement = vi.fn();
  const { result } = renderHook(() => useIdempotentAction(async () => {}));
  await act(() => result.current.execute("Accept", original));
  expect(result.current.uncertain).toBe("Accept");
  await act(() => result.current.execute("Accept", replacement));
  expect(original).toHaveBeenCalledTimes(2);
  expect(original.mock.calls[0][0]).toBe(original.mock.calls[1][0]);
  expect(replacement).not.toHaveBeenCalled();
});

test("an older query cannot replace a newer manual refresh", async () => {
  let first!: (value: string) => void;
  let second!: (value: string) => void;
  const read = vi.fn().mockImplementationOnce(() => new Promise<string>(resolve => { first = resolve; }))
    .mockImplementationOnce(() => new Promise<string>(resolve => { second = resolve; }));
  const { result } = renderHook(() => useServerQuery(read));
  let refreshed!: Promise<void>;
  act(() => { refreshed = result.current.refresh(); });
  await act(async () => { second("current"); await refreshed; });
  await act(async () => { first("stale"); });
  expect(result.current.data).toBe("current");
});
