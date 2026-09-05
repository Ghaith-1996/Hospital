import React from "react";
import { fireEvent, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import AlertDetailsPage from "../app/alerts/[id]/page";
import AlertReviewPage from "../app/alerts/[id]/review/page";
import AlertSentPage from "../app/alerts/[id]/sent/page";
import { createSeedState } from "../features/alerts/seed";
import { selectAlertById } from "../features/alerts/selectors";
import { usePrototype } from "../features/alerts/prototype-store";
import type { PrototypeState } from "../features/alerts/types";
import { renderPrototype } from "./test-utils";

export const mockPush = vi.fn();
const mockParams = { id: "alert-critical-1" };

vi.mock("next/navigation", () => ({
  useParams: () => mockParams,
  useRouter: () => ({
    push: mockPush,
  }),
}));

function AlertStatusProbe({ id }: { id: string }) {
  const { state } = usePrototype();
  const alert = selectAlertById(state, id);

  return (
    <output data-testid={`alert-status-${id}`}>
      {alert
        ? `${alert.status}|${alert.deliveryState}|${alert.recipients.map((recipient) => recipient.response).join(",")}`
        : "missing"}
    </output>
  );
}

function createDraftReviewState(): PrototypeState {
  const state = createSeedState();

  return {
    ...state,
    alerts: state.alerts.map((alert) =>
      alert.id !== "alert-critical-1"
        ? alert
        : {
            ...alert,
            status: "draft",
            activities: alert.activities.filter((activity) => activity.kind !== "sent"),
          },
    ),
  };
}

describe("review confirmation and sent state", () => {
  beforeEach(() => {
    mockPush.mockClear();
    mockParams.id = "alert-critical-1";
    window.history.pushState({}, "", "/alerts/alert-critical-1/review");
  });

  it("renders the source-backed review, confirms deliberately, and routes to the sent state", () => {
    renderPrototype(
      <>
        <AlertReviewPage />
        <AlertStatusProbe id="alert-critical-1" />
      </>,
      { state: createDraftReviewState() },
    );

    expect(screen.getByRole("heading", { name: "Review & Confirm Alert" })).toBeVisible();
    expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
    expect(screen.getByText("Dr. Marc Tremblay")).toBeVisible();
    expect(screen.getByText("Dr. Julie Martin")).toBeVisible();
    expect(screen.getByText("Dr. David Nguyen")).toBeVisible();

    fireEvent.click(screen.getByRole("button", { name: "Confirm & Dispatch" }));

    const dialog = screen.getByRole("dialog", { name: "Confirm alert dispatch?" });
    expect(dialog).toBeVisible();
    expect(within(dialog).getByText(/send this fictional alert to 3 clinicians/i)).toBeVisible();
    expect(within(dialog).getByRole("button", { name: "Cancel" })).toHaveFocus();
    expect(screen.getByTestId("alert-status-alert-critical-1")).toHaveTextContent("draft|not-observed|none,none,none");

    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm fictional dispatch" }));

    expect(mockPush).toHaveBeenCalledWith("/alerts/alert-critical-1/sent");
    expect(screen.getByTestId("alert-status-alert-critical-1")).toHaveTextContent("sent|not-observed|none,none,none");
  });

  it("cancels with buttons and Escape, wraps focus, and returns focus to the trigger", () => {
    renderPrototype(<AlertReviewPage />, { state: createDraftReviewState() });

    const trigger = screen.getByRole("button", { name: "Confirm & Dispatch" });
    trigger.focus();
    fireEvent.click(trigger);

    const firstDialog = screen.getByRole("dialog", { name: "Confirm alert dispatch?" });
    const cancelButton = within(firstDialog).getByRole("button", { name: "Cancel" });
    const confirmButton = within(firstDialog).getByRole("button", { name: "Confirm fictional dispatch" });

    expect(cancelButton).toHaveFocus();

    fireEvent.keyDown(firstDialog, { key: "Tab", shiftKey: true });
    expect(confirmButton).toHaveFocus();

    fireEvent.keyDown(firstDialog, { key: "Tab" });
    expect(cancelButton).toHaveFocus();

    fireEvent.click(screen.getByTestId("confirm-dialog-backdrop"));
    expect(screen.getByRole("dialog", { name: "Confirm alert dispatch?" })).toBeVisible();

    fireEvent.click(cancelButton);
    expect(screen.queryByRole("dialog", { name: "Confirm alert dispatch?" })).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();

    fireEvent.click(trigger);
    const secondDialog = screen.getByRole("dialog", { name: "Confirm alert dispatch?" });
    fireEvent.keyDown(secondDialog, { key: "Escape" });

    expect(screen.queryByRole("dialog", { name: "Confirm alert dispatch?" })).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
    expect(mockPush).not.toHaveBeenCalled();
  });

  it("renders a not-found state when the reviewed alert is missing", () => {
    mockParams.id = "missing-alert";
    window.history.pushState({}, "", "/alerts/missing-alert/review");

    renderPrototype(<AlertReviewPage />);

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Create another alert" })).toHaveAttribute("href", "/alerts/new");
  });

  it("renders the sent success state and its two follow-up actions", () => {
    window.history.pushState({}, "", "/alerts/alert-critical-1/sent");

    renderPrototype(<AlertSentPage />);

    expect(screen.getByRole("heading", { name: "Alert Sent Successfully!" })).toBeVisible();
    expect(screen.getByText(/simulated sending to 3 fictional clinicians/i)).toBeVisible();
    expect(screen.getByText("What happens next?")).toBeVisible();
    expect(screen.getByText(/delivery, opened, acknowledged, and accepted stay separate/i)).toBeVisible();
    expect(screen.getByRole("link", { name: "View Alert Details" })).toHaveAttribute("href", "/alerts/alert-critical-1");
    expect(screen.getByRole("link", { name: "Create Another Alert" })).toHaveAttribute("href", "/alerts/new");
  });

  it("routes the sent primary action to an implemented alert details page", () => {
    window.history.pushState({}, "", "/alerts/alert-critical-1");

    renderPrototype(<AlertDetailsPage />);

    expect(screen.getByRole("heading", { name: "Alert Details" })).toBeVisible();
    expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
    expect(screen.getByRole("region", { name: "Responses Summary" })).toBeVisible();
    expect(screen.getByText(/delivery, acknowledgement, and responsibility acceptance remain separate/i)).toBeVisible();
  });

  it("renders a not-found state when the sent alert is missing", () => {
    mockParams.id = "missing-alert";
    window.history.pushState({}, "", "/alerts/missing-alert/sent");

    renderPrototype(<AlertSentPage />);

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Create another alert" })).toHaveAttribute("href", "/alerts/new");
  });
});
