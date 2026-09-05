import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test, vi } from "vitest";
import { DirectoryImport } from "../features/connected/directory-import";
import { applyDirectoryImport } from "../lib/directory-import";
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

test("an uncertain apply retains the reviewed file and token for retry", async () => {
  vi.mocked(applyDirectoryImport).mockRejectedValue(new TypeError("offline"));
  render(<DirectoryImport />);
  const file = new File(["fictional"], "retry.csv");
  fireEvent.change(screen.getByLabelText("Simulation CSV"), { target: { files: [file] } });
  fireEvent.click(screen.getByRole("button", { name: "Preview import" }));
  await screen.findByText(/Preview ready/);
  fireEvent.click(screen.getByRole("button", { name: "Apply import" }));
  await screen.findByRole("alert");
  expect(screen.getByRole("button", { name: "Apply import" })).toBeEnabled();
  fireEvent.click(screen.getByRole("button", { name: "Apply import" }));
  expect(applyDirectoryImport).toHaveBeenNthCalledWith(1, file, "server-preview");
  expect(applyDirectoryImport).toHaveBeenNthCalledWith(2, file, "server-preview");
});
