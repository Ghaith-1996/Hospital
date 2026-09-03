import React from "react";
import { render } from "@testing-library/react";
import { createSeedState } from "../features/alerts/seed";
import { PrototypeProvider } from "../features/alerts/prototype-store";
import type { PrototypeState } from "../features/alerts/types";

export function renderPrototype(
  ui: React.ReactElement,
  options: {
    state?: PrototypeState;
    storage?: Pick<Storage, "getItem" | "setItem" | "removeItem">;
  } = {},
) {
  return render(
    <PrototypeProvider initialState={options.state ?? createSeedState()} storage={options.storage}>
      {ui}
    </PrototypeProvider>,
  );
}
