import React from "react";
import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import DoctorAlertPage from "../app/my-alerts/[id]/page";
import { createSeedState } from "../features/alerts/seed";
import { selectAlertById } from "../features/alerts/selectors";
import { prototypeReducer, usePrototype } from "../features/alerts/prototype-store";
import type { PrototypeState } from "../features/alerts/types";
import { renderPrototype } from "./test-utils";

const navigation = {
  params: { id: "alert-critical-1" as string | string[] | undefined },
};

export const mockPush = vi.fn();

vi.mock("next/navigation", () => ({
  useParams: () => navigation.params,
  useRouter: () => ({
    push: mockPush,
  }),
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

vi.mock("next/link", () => ({
  default: ({
    href,
    children,
    className,
    "aria-label": ariaLabel,
  }: {
    href: string;
    children: React.ReactNode;
    className?: string;
    "aria-label"?: string;
  }) => React.createElement("a", { href, className, "aria-label": ariaLabel }, children),
}));

function marcState(): PrototypeState {
  return {
    ...createSeedState(),
    selectedUserId: "user-marc",
  };
}

function acceptedMarcState(): PrototypeState {
  return prototypeReducer(marcState(), {
    type: "doctor-responded",
    alertId: "alert-critical-1",
    clinicianId: "clinician-marc",
    response: "accepted",
    note: "SIMULATION: taking this fictional case.",
    occurredAt: "2026-08-30T14:24:00.000Z",
  });
}

function ResponseProbe() {
  const { state } = usePrototype();
  const alert = selectAlertById(state, "alert-critical-1");
  const marc = alert?.recipients.find((recipient) => recipient.clinicianId === "clinician-marc");

  return <output data-testid="marc-response">{marc?.response ?? "missing"}</output>;
}

describe("doctor alert detail route", () => {
  beforeEach(() => {
    navigation.params.id = "alert-critical-1";
    mockPush.mockClear();
    window.history.pushState({}, "", "/my-alerts/alert-critical-1");
  });

  it("renders the selected doctor's fictional alert details and sticky response controls", () => {
    renderPrototype(<DoctorAlertPage />, { state: marcState() });

    expect(screen.getByRole("heading", { name: "Chest pain, hypotension" })).toBeVisible();
    expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
    expect(screen.getByText("Fictional ER - Simulation Bed 12")).toBeVisible();
    expect(screen.getByText("Fictional Emergency")).toBeVisible();
    expect(screen.getByText(/Need cardiology evaluation and possible cath lab activation/)).toBeVisible();
    expect(screen.getByText("Received: Aug 30, 2026, 2:11 PM")).toBeVisible();
    expect(screen.getAllByText("Critical").length).toBeGreaterThan(0);

    const recipients = screen.getByRole("region", { name: "Other Recipients" });
    expect(within(recipients).getByText("Dr. Julie Martin")).toBeVisible();
    expect(within(recipients).getByText("Dr. David Nguyen")).toBeVisible();
    expect(within(recipients).queryByText("Dr. Marc Tremblay")).not.toBeInTheDocument();

    const responseRegion = screen.getByRole("region", { name: "Respond to this fictional alert" });
    expect(responseRegion).toHaveClass("response-panel");
    expect(responseRegion).toHaveClass("response-panel--sticky");
    expect(within(responseRegion).getByRole("button", { name: "Acknowledge" })).toBeVisible();
    expect(within(responseRegion).getByRole("button", { name: "Accept" })).toBeVisible();
    expect(within(responseRegion).getByRole("button", { name: "Decline" })).toBeVisible();
    expect(within(responseRegion).getByRole("button", { name: "Unavailable" })).toBeVisible();
    expect(within(responseRegion).getByRole("button", { name: "More" })).toHaveAttribute("title", "Coming later");
  });

  it("routes response actions to the focused response page without mutating first", () => {
    renderPrototype(
      <>
        <DoctorAlertPage />
        <ResponseProbe />
      </>,
      { state: marcState() },
    );

    fireEvent.click(screen.getByRole("button", { name: "Accept" }));

    expect(mockPush).toHaveBeenCalledWith("/my-alerts/alert-critical-1/respond?response=accepted");
    expect(screen.getByTestId("marc-response")).toHaveTextContent("none");
  });

  it("renders an accessible success message and current response state after submission", () => {
    window.history.pushState({}, "", "/my-alerts/alert-critical-1?responded=1");

    renderPrototype(<DoctorAlertPage />, { state: acceptedMarcState() });

    expect(screen.getByRole("status", { name: "Fictional response saved" })).toBeVisible();
    expect(screen.getByText("Your current response: Accepted")).toBeVisible();
    expect(screen.getByText("Responsibility accepted in this local simulation.")).toBeVisible();
  });

  it("renders a not-found state instead of an empty detail page", () => {
    navigation.params.id = "missing-alert";
    window.history.pushState({}, "", "/my-alerts/missing-alert");

    renderPrototype(<DoctorAlertPage />, { state: marcState() });

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Back to Inbox" })).toHaveAttribute("href", "/my-alerts");
  });
});
