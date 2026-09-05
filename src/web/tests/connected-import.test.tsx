import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { DirectoryImport } from "../features/connected/directory-import";
vi.mock("../lib/directory-import", () => ({ previewDirectoryImport: vi.fn().mockResolvedValue({ sourceSystem: "SIM-CSV", parsedPractitionerCount: 12, insertCount: 0, updateCount: 12, rejectedCount: 0, errors: [], warnings: [], changes: [], previewToken: "server-preview" }), applyDirectoryImport: vi.fn() }));
test("changing the CSV invalidates a server preview and disables apply", async () => {
  render(<DirectoryImport />);
  const upload = screen.getByLabelText("Simulation CSV");
  fireEvent.change(upload, { target: { files: [new File(["fictional"], "first.csv")] } });
  fireEvent.click(screen.getByRole("button", { name: "Preview import" }));
  expect(await screen.findByText(/Preview ready for SIM-CSV/)).toBeVisible();
  expect(screen.getByRole("button", { name: "Apply import" })).toBeEnabled();
  fireEvent.change(upload, { target: { files: [new File(["changed"], "second.csv")] } });
  expect(screen.getByRole("button", { name: "Apply import" })).toBeDisabled();
  expect(screen.queryByText(/Preview ready/)).not.toBeInTheDocument();
});
