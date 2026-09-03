import React from "react";
import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AlertDetailsPage from "../app/alerts/[id]/page";
import RespondToAlertPage from "../app/my-alerts/[id]/respond/page";
import { createSeedState, DEMO_NOW } from "../features/alerts/seed";
import { selectAlertById } from "../features/alerts/selectors";
import { loadPrototypeState } from "../features/alerts/prototype-store";
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

function renderRespond(path = "/my-alerts/alert-critical-1/respond") {
  window.history.pushState({}, "", path);
  return renderPrototype(<RespondToAlertPage />, { state: marcState() });
}

describe("respond to alert route", () => {
  beforeEach(() => {
    navigation.params.id = "alert-critical-1";
    mockPush.mockClear();
    localStorage.clear();
    window.history.pushState({}, "", "/my-alerts/alert-critical-1/respond");
  });

  it("renders the exact response choices and defaults Accept from the accepted query value", () => {
    renderRespond("/my-alerts/alert-critical-1/respond?response=accepted");

    expect(screen.getByRole("heading", { name: "Respond to Alert" })).toBeVisible();
    expect(screen.getByText("Chest pain, hypotension")).toBeVisible();

    const acknowledge = screen.getByRole("radio", { name: "Acknowledge" });
    const accept = screen.getByRole("radio", { name: "Accept" });
    const decline = screen.getByRole("radio", { name: "Decline" });
    const unavailable = screen.getByRole("radio", { name: "Unavailable" });

    expect(acknowledge).toBeVisible();
    expect(accept).toBeChecked();
    expect(decline).toBeVisible();
    expect(unavailable).toBeVisible();
    expect(screen.getByText("I have received this alert.")).toBeVisible();
    expect(screen.getByText("I will take responsibility for this fictional case.")).toBeVisible();
    expect(screen.getByText("I am not able to take this fictional case.")).toBeVisible();
    expect(screen.getByText("I am currently unavailable.")).toBeVisible();
    expect(screen.getByLabelText("Add a Note (optional)")).toBeVisible();
    expect(screen.getByText("0 / 500 characters")).toBeVisible();
    expect(screen.getByRole("link", { name: "Cancel" })).toHaveAttribute("href", "/my-alerts/alert-critical-1");
    expect(screen.getByRole("button", { name: "Submit Response" })).toBeVisible();
  });

  it("clamps the optional note to 500 characters and updates the live count", () => {
    renderRespond();

    fireEvent.change(screen.getByLabelText("Add a Note (optional)"), { target: { value: "x".repeat(520) } });

    expect(screen.getByLabelText("Add a Note (optional)")).toHaveValue("x".repeat(500));
    expect(screen.getByText("500 / 500 characters")).toBeVisible();
  });

  it("submits acknowledgement without accepting responsibility and updates operator details through canonical state", async () => {
    renderPrototype(
      <>
        <RespondToAlertPage />
        <AlertDetailsPage />
      </>,
      { state: marcState() },
    );

    fireEvent.click(screen.getByRole("radio", { name: "Acknowledge" }));
    fireEvent.change(screen.getByLabelText("Add a Note (optional)"), {
      target: { value: "SIMULATION: received this fictional alert." },
    });
    fireEvent.click(screen.getByRole("button", { name: "Submit Response" }));

    await waitFor(() => {
      expect(selectAlertById(loadPrototypeState(), "alert-critical-1")?.recipients[0].response).toBe("acknowledged");
    });
    const alert = selectAlertById(loadPrototypeState(), "alert-critical-1");
    expect(alert?.recipients[0].responsibilityAcceptedAt).toBeUndefined();
    expect(alert?.recipients[0].note).toBe("SIMULATION: received this fictional alert.");
    expect(alert?.activities.filter((activity) => activity.kind === "acknowledged")).toHaveLength(1);
    expect(alert?.activities[0]).toMatchObject({
      kind: "acknowledged",
      occurredAt: DEMO_NOW,
    });
    expect(alert?.recipients[1].response).toBe("none");
    expect(alert?.recipients[2].response).toBe("none");
    expect(mockPush).toHaveBeenCalledWith("/my-alerts/alert-critical-1?responded=1");

    const summary = screen.getByRole("region", { name: "Responses Summary" });
    expect(within(summary).getByText("1 acknowledged receipt")).toBeVisible();
    expect(within(summary).getByText("0 accepted responsibility")).toBeVisible();
  });

  it("submits Accept with responsibility acceptance for only the selected doctor", async () => {
    renderRespond("/my-alerts/alert-critical-1/respond?response=accepted");

    fireEvent.click(screen.getByRole("button", { name: "Submit Response" }));

    await waitFor(() => {
      expect(selectAlertById(loadPrototypeState(), "alert-critical-1")?.recipients[0].response).toBe("accepted");
    });
    const alert = selectAlertById(loadPrototypeState(), "alert-critical-1");

    expect(alert?.status).toBe("in-progress");
    expect(alert?.recipients[0].responsibilityAcceptedAt).toBe(DEMO_NOW);
    expect(alert?.recipients[1].responsibilityAcceptedAt).toBeUndefined();
    expect(alert?.recipients[2].responsibilityAcceptedAt).toBeUndefined();
    expect(alert?.activities.filter((activity) => activity.kind === "accepted")).toHaveLength(1);
    expect(mockPush).toHaveBeenCalledWith("/my-alerts/alert-critical-1?responded=1");
  });

  it("renders a not-found state when the response route cannot find the alert", () => {
    navigation.params.id = "missing-alert";
    window.history.pushState({}, "", "/my-alerts/missing-alert/respond");

    renderPrototype(<RespondToAlertPage />, { state: marcState() });

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Back to Inbox" })).toHaveAttribute("href", "/my-alerts");
  });

  it("does not offer submit controls when the selected doctor is not a recipient", () => {
    navigation.params.id = "alert-in-progress-1";
    window.history.pushState({}, "", "/my-alerts/alert-in-progress-1/respond?response=accepted");

    renderPrototype(<RespondToAlertPage />, {
      state: {
        ...createSeedState(),
        selectedUserId: "user-julie",
      },
    });

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByText("This fictional alert is not assigned to the selected doctor.")).toBeVisible();
    expect(screen.queryByRole("button", { name: "Submit Response" })).not.toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "Accept" })).not.toBeInTheDocument();
    expect(mockPush).not.toHaveBeenCalled();
  });
});
