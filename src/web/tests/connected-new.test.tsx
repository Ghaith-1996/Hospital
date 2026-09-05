import React from "react";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { NewAlert } from "../features/connected/new-alert";
import { createAlertDraft } from "../lib/alerts";
vi.mock("../lib/alerts", async original => ({ ...await original<typeof import("../lib/alerts")>(), createAlertDraft: vi.fn().mockResolvedValue({ alertId: "server-id" }) }));
vi.mock("../lib/development-auth", () => ({ getSimulationLocationContext: vi.fn().mockResolvedValue({ sites: [{ siteId: "server-site", name: "Fictional Site", departments: [{ departmentId: "server-dept", name: "Fictional Department" }] }] }) }));
const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
test("creates from server location choices and navigates to the returned backend draft", async () => {
  render(<NewAlert />);
  fireEvent.change(await screen.findByLabelText("Simulation site"), { target: { value: "server-site" } });
  fireEvent.change(screen.getByLabelText("Simulation department"), { target: { value: "server-dept" } });
  for (const label of ["Fictional patient reference", "Fictional location", "Operator-selected DEMO urgency", "Source text", "Situation", "Background", "Assessment", "Recommendation"]) {
    fireEvent.change(screen.getByLabelText(label), { target: { value: label === "Fictional patient reference" ? "SIM-PAT-1" : "SIMULATION: fictional content" } });
  }
  fireEvent.click(screen.getByRole("button", { name: "Create backend draft" }));
  await waitFor(() => expect(push).toHaveBeenCalledWith("/alerts/server-id/compose"));
  expect(createAlertDraft).toHaveBeenCalledWith(expect.objectContaining({ siteId: "server-site", departmentId: "server-dept", simulationPatientReference: "SIM-PAT-1" }));
});
