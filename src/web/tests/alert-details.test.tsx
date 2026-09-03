import React from "react";
import { screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ActivityTimeline } from "../components/alerts/activity-timeline";
import { EscalationTimeline } from "../components/alerts/escalation-timeline";
import { ResponseSummary } from "../components/alerts/response-summary";
import AlertDetailsPage from "../app/alerts/[id]/page";
import { createSeedState } from "../features/alerts/seed";
import type { AlertActivity, AlertRecord } from "../features/alerts/types";
import { renderPrototype } from "./test-utils";

const navigation = {
  params: { id: "alert-critical-1" as string | string[] | undefined },
};

vi.mock("next/navigation", () => ({
  useParams: () => navigation.params,
}));

describe("alert details monitoring route", () => {
  beforeEach(() => {
    navigation.params.id = "alert-critical-1";
    window.history.pushState({}, "", "/alerts/alert-critical-1");
  });

  it("renders alert details with activity and response regions from the canonical alert", () => {
    renderPrototype(<AlertDetailsPage />);

    expect(screen.getByRole("heading", { name: "Alert Details" })).toBeVisible();
    expect(screen.getByText("SIM-PAT-01578")).toBeVisible();
    expect(screen.getByRole("region", { name: "Activity Timeline" })).toBeVisible();
    expect(screen.getByRole("region", { name: "Responses Summary" })).toBeVisible();
    expect(screen.getByText("Acknowledged")).toBeVisible();
    expect(screen.getByText("Accepted")).toBeVisible();
    expect(screen.getAllByText("Not observed").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Dr. Marc Tremblay").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Dr. Julie Martin").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Dr. David Nguyen").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: "View Policy - Coming later" })).toBeDisabled();
    expect(screen.getByText("Coming later: fictional escalation policy review is not connected in this prototype.")).toBeVisible();
  });

  it("renders the fixed demo escalation branch without live update copy", () => {
    navigation.params.id = "alert-escalating-1";
    window.history.pushState({}, "", "/alerts/alert-escalating-1");

    renderPrototype(<AlertDetailsPage />);

    expect(screen.getByRole("heading", { name: "Alert Escalation" })).toBeVisible();
    expect(screen.getByText("DEMO elapsed time: 12 min")).toBeVisible();
    expect(screen.getByText("Escalating to fictional on-call cardiologist")).toBeVisible();
    expect(screen.getAllByText("Submitted").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Critical").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Escalating").length).toBeGreaterThan(0);
    expect(screen.queryByText(/next update in/i)).not.toBeInTheDocument();
  });

  it("renders an accessible not-found state when the alert is absent", () => {
    navigation.params.id = "missing-alert";
    window.history.pushState({}, "", "/alerts/missing-alert");

    renderPrototype(<AlertDetailsPage />);

    expect(screen.getByRole("status", { name: "Fictional alert not found" })).toBeVisible();
    expect(screen.getByRole("link", { name: "Back to Alerts" })).toHaveAttribute("href", "/alerts");
  });

  it("prints every explicit delivery state label", () => {
    const state = createSeedState();
    const [baseAlert] = state.alerts;
    const deliveryStates: Array<AlertRecord["deliveryState"]> = [
      "not-observed",
      "submitted",
      "delivered",
      "failed",
      "not-applicable",
    ];

    renderPrototype(
      <>
        {deliveryStates.map((deliveryState) => (
          <ResponseSummary
            key={deliveryState}
            alert={{ ...baseAlert, deliveryState }}
            clinicians={state.clinicians}
          />
        ))}
      </>,
    );

    expect(screen.getByText("Not observed")).toBeVisible();
    expect(screen.getByText("Submitted")).toBeVisible();
    expect(screen.getByText("Delivered")).toBeVisible();
    expect(screen.getByText("Failed")).toBeVisible();
    expect(screen.getByText("Not applicable")).toBeVisible();
  });
});

describe("ActivityTimeline", () => {
  it("sorts a copied activity array ascending and renders visible times and labels in a semantic list", () => {
    const activities: AlertActivity[] = [
      {
        id: "late",
        kind: "accepted",
        label: "SIMULATION: fictional acceptance recorded.",
        occurredAt: "2026-08-30T14:20:00.000Z",
        tone: "success",
      },
      {
        id: "early",
        kind: "created",
        label: "SIMULATION: fictional draft created.",
        occurredAt: "2026-08-30T13:10:00.000Z",
        tone: "neutral",
      },
      {
        id: "middle",
        kind: "sent",
        label: "SIMULATION: fictional alert submitted.",
        occurredAt: "2026-08-30T13:40:00.000Z",
        tone: "info",
      },
    ];
    const originalOrder = activities.map((activity) => activity.id);

    renderPrototype(<ActivityTimeline activities={activities} />);

    const list = screen.getByRole("list", { name: "Activity Timeline" });
    const items = within(list).getAllByRole("listitem");

    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("1:10 PM");
    expect(items[0]).toHaveTextContent("SIMULATION: fictional draft created.");
    expect(items[1]).toHaveTextContent("1:40 PM");
    expect(items[1]).toHaveTextContent("SIMULATION: fictional alert submitted.");
    expect(items[2]).toHaveTextContent("2:20 PM");
    expect(items[2]).toHaveTextContent("SIMULATION: fictional acceptance recorded.");
    expect(activities.map((activity) => activity.id)).toEqual(originalOrder);
  });
});

describe("ResponseSummary", () => {
  it("separately counts accepted, acknowledged, unavailable or declined, and no response", () => {
    const state = createSeedState();
    const [baseAlert] = state.alerts;

    renderPrototype(
      <ResponseSummary
        alert={{
          ...baseAlert,
          deliveryState: "delivered",
          recipients: [
            { clinicianId: "clinician-marc", response: "accepted" },
            { clinicianId: "clinician-julie", response: "acknowledged" },
            { clinicianId: "clinician-david", response: "declined" },
            { clinicianId: "clinician-missing", response: "unavailable" },
            { clinicianId: "clinician-unseen", response: "none" },
          ],
        }}
        clinicians={state.clinicians}
      />,
    );

    expect(screen.getByText("Accepted")).toBeVisible();
    expect(screen.getByText("1 accepted responsibility")).toBeVisible();
    expect(screen.getByText("Acknowledged")).toBeVisible();
    expect(screen.getByText("1 acknowledged receipt")).toBeVisible();
    expect(screen.getByText("Declined / unavailable")).toBeVisible();
    expect(screen.getByText("2 declined or unavailable")).toBeVisible();
    expect(screen.getByText("No response")).toBeVisible();
    expect(screen.getByText("1 no response yet")).toBeVisible();
    expect(screen.getByText("Acknowledgement confirms receipt only; it does not accept responsibility.")).toBeVisible();
  });
});

describe("EscalationTimeline", () => {
  it("renders fixed vertical steps with textual Complete, In progress, and Pending states", () => {
    const state = createSeedState();
    const escalatingAlert = state.alerts.find((alert) => alert.id === "alert-escalating-1");

    renderPrototype(<EscalationTimeline steps={escalatingAlert?.escalationSteps ?? []} />);

    const list = screen.getByRole("list", { name: "Alert Escalation" });
    const items = within(list).getAllByRole("listitem");

    expect(items).toHaveLength(3);
    expect(items[0]).toHaveTextContent("Complete");
    expect(items[1]).toHaveTextContent("In progress");
    expect(items[2]).toHaveTextContent("Pending");
    expect(screen.queryByText(/next update in/i)).not.toBeInTheDocument();
  });
});
