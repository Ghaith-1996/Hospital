import React from "react";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AlertReviewPage from "../app/alerts/[id]/review/page";
import NewAlertPage from "../app/alerts/new/page";
import { createSeedState } from "../features/alerts/seed";
import { selectAlertById } from "../features/alerts/selectors";
import { usePrototype } from "../features/alerts/prototype-store";
import { renderPrototype } from "./test-utils";

export const mockPush = vi.fn();
export const mockReplace = vi.fn((href: string) => {
  window.history.replaceState({}, "", href);
});

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: mockPush,
    replace: mockReplace,
  }),
  useParams: () => ({
    id: "alert-custom-1",
  }),
}));

function AlertProbe({ id }: { id: string }) {
  const { state } = usePrototype();
  const alert = selectAlertById(state, id);

  return (
    <output data-testid={`alert-probe-${id}`}>
      {alert
        ? `${alert.patientReference}|${alert.location}|${alert.department}|${alert.urgency}|${alert.caseDetails}|${alert.recipients
            .map((recipient) => recipient.clinicianId)
            .join(",")}`
        : "missing"}
    </output>
  );
}

function renderNewAlert(path = "/alerts/new") {
  window.history.pushState({}, "", path);
  return renderPrototype(<NewAlertPage />);
}

describe("new alert workflow", () => {
  beforeEach(() => {
    mockPush.mockClear();
    mockReplace.mockClear();
    window.history.pushState({}, "", "/alerts/new");
  });

  it("shows validation only after submit and requires one clinician", async () => {
    renderNewAlert();
    expect(screen.queryByText("Patient reference is required.")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "" } });
    fireEvent.change(screen.getByLabelText("Case Details"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

    expect(screen.getByText("Patient reference is required.")).toBeVisible();
    expect(screen.getByText("Case details are required.")).toBeVisible();
    expect(screen.getByText("Select at least one fictional clinician.")).toBeVisible();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it("searches, selects, removes, and creates one local draft", () => {
    renderNewAlert();

    fireEvent.change(screen.getByLabelText("Search fictional clinicians"), { target: { value: "emergency" } });
    fireEvent.click(screen.getByRole("button", { name: "Add Dr. Marc Tremblay" }));

    expect(screen.getByText("Selected Clinicians (1)")).toBeVisible();
    expect(screen.getByTestId("alert-summary")).toHaveTextContent("Dr. Marc Tremblay");

    fireEvent.click(screen.getByRole("button", { name: "Remove Dr. Marc Tremblay" }));
    expect(screen.getByText("Selected Clinicians (0)")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Add Dr. Marc Tremblay" }));
    fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "SIM-PAT-4400" } });
    fireEvent.change(screen.getByLabelText("Case Details"), {
      target: { value: "SIMULATION: fictional critical potassium requiring review." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

    expect(mockPush).toHaveBeenCalledWith(expect.stringMatching(/^\/alerts\/alert-[a-z0-9-]+\/review$/));
  });

  it("preserves typed fields after a validation failure", () => {
    renderNewAlert();

    fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "SIM-PAT-7788" } });
    fireEvent.change(screen.getByLabelText("Case Details"), {
      target: { value: "SIMULATION: fictional dyspnea with rising oxygen needs." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

    expect(screen.getByText("Select at least one fictional clinician.")).toBeVisible();
    expect(screen.getByLabelText("Patient Reference")).toHaveValue("SIM-PAT-7788");
    expect(screen.getByLabelText("Case Details")).toHaveValue("SIMULATION: fictional dyspnea with rising oxygen needs.");
  });

  it("toggles Type and Dictate without connecting dictation", () => {
    renderNewAlert();

    const typeButton = screen.getByRole("button", { name: "Type" });
    const dictateButton = screen.getByRole("button", { name: "Dictate" });
    expect(typeButton).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(dictateButton);

    expect(dictateButton).toHaveAttribute("aria-pressed", "true");
    expect(
      screen.getByText(
        "Dictation is not connected in this frontend prototype. Type the fictional case details instead.",
      ),
    ).toBeVisible();
    expect(screen.queryByLabelText("Case Details")).not.toBeInTheDocument();

    fireEvent.click(typeButton);

    expect(screen.getByLabelText("Case Details")).toBeVisible();
  });

  it("clamps case details to 4000 characters and shows the counter", () => {
    renderNewAlert();

    fireEvent.change(screen.getByLabelText("Case Details"), { target: { value: "x".repeat(4010) } });

    expect(screen.getByLabelText("Case Details")).toHaveValue("x".repeat(4000));
    expect(screen.getByText("4000/4000 characters")).toBeVisible();
  });

  it("clears fields, selected clinicians, and clinician search", () => {
    renderNewAlert();

    fireEvent.change(screen.getByLabelText("Patient Reference"), { target: { value: "SIM-PAT-5512" } });
    fireEvent.change(screen.getByLabelText("Case Details"), {
      target: { value: "SIMULATION: fictional fever and hypotension." },
    });
    fireEvent.change(screen.getByLabelText("Search fictional clinicians"), { target: { value: "cardiology" } });
    fireEvent.click(screen.getByRole("button", { name: "Add Dr. Julie Martin" }));

    fireEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(screen.getByLabelText("Patient Reference")).toHaveValue("");
    expect(screen.getByLabelText("Case Details")).toHaveValue("");
    expect(screen.getByLabelText("Search fictional clinicians")).toHaveValue("");
    expect(screen.getByText("Selected Clinicians (0)")).toBeVisible();
    expect(screen.getByTestId("alert-summary")).toHaveTextContent("None selected");
  });

  it("clears edit mode and removes the edit query so the form stays empty", async () => {
    renderNewAlert("/alerts/new?edit=alert-critical-1");

    expect(await screen.findByDisplayValue("SIM-PAT-01578")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Clear" }));

    expect(mockReplace).toHaveBeenCalledWith("/alerts/new");
    expect(window.location.pathname).toBe("/alerts/new");
    expect(window.location.search).toBe("");
    expect(screen.getByLabelText("Patient Reference")).toHaveValue("");
    expect(screen.getByLabelText("Case Details")).toHaveValue("");
    expect(screen.getByText("Selected Clinicians (0)")).toBeVisible();
  });

  it("loads an edit query, updates the canonical alert, and returns to the same review route", async () => {
    window.history.pushState({}, "", "/alerts/new?edit=alert-critical-1");
    renderPrototype(
      <>
        <NewAlertPage />
        <AlertProbe id="alert-critical-1" />
      </>,
      { state: createSeedState() },
    );

    expect(await screen.findByDisplayValue("SIM-PAT-01578")).toBeVisible();
    expect(screen.getByLabelText("Case Details")).toHaveValue(
      "SIMULATION: fictional 66-year-old male with chest pain and shortness of breath for 30 minutes.\nBP 170/94, HR 128, SpO2 86% on 2L O2.\nReceived ASA 325 mg and NTG x1 with no relief.\nPast history: fictional hypertension and type 2 diabetes.\nNeed cardiology evaluation and possible cath lab activation.",
    );
    expect(screen.getByText("Selected Clinicians (3)")).toBeVisible();
    expect(within(screen.getByTestId("alert-summary")).getByText("Dr. Marc Tremblay")).toBeVisible();

    fireEvent.change(screen.getByLabelText("Case Details"), {
      target: { value: "SIMULATION: fictional updated chest pain narrative." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Review & Confirm" }));

    expect(mockPush).toHaveBeenCalledWith("/alerts/alert-critical-1/review");
    await waitFor(() => {
      expect(screen.getByTestId("alert-probe-alert-critical-1")).toHaveTextContent(
        "SIM-PAT-01578|Fictional ER - Simulation Bed 12|Fictional Emergency|critical|SIMULATION: fictional updated chest pain narrative.|clinician-marc,clinician-julie,clinician-david",
      );
    });
  });

  it("shows a not-found review state for a missing local alert", () => {
    window.history.pushState({}, "", "/alerts/alert-custom-1/review");

    renderPrototype(<AlertReviewPage />);

    expect(mockReplace).not.toHaveBeenCalled();
    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Create another alert" })).toHaveAttribute("href", "/alerts/new");
  });
});
