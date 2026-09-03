import React from "react";
import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createSeedState, DEMO_NOW, STORAGE_KEY } from "../features/alerts/seed";
import {
  type PrototypeAction,
  buildAlert,
  loadPrototypeState,
  prototypeReducer,
  savePrototypeState,
  usePrototype,
} from "../features/alerts/prototype-store";
import type { DoctorResponse, NewAlertInput, PrototypeState } from "../features/alerts/types";
import { searchClinicians, selectAlertById, selectAlerts, selectDoctorAlerts } from "../features/alerts/selectors";
import { renderPrototype } from "./test-utils";

const draftInput: NewAlertInput = {
  patientReference: "SIM-PAT-9001",
  location: "North Wing / Simulation Room 12",
  department: "Fictional Emergency",
  urgency: "critical",
  caseDetails: "SIMULATION: fictional chest pain and hypotension.",
  clinicianIds: ["clinician-marc"],
};

function PrototypeProbe() {
  const { state, storageError, selectUser } = usePrototype();

  return (
    <div>
      <p>selected-user:{state.selectedUserId}</p>
      <p>storage-error:{storageError ?? "none"}</p>
      <button type="button" onClick={() => selectUser("user-marc")}>
        Switch user
      </button>
    </div>
  );
}

function createMemoryStorage(initialState?: PrototypeState) {
  let stored = initialState ? JSON.stringify(initialState) : null;
  return {
    getItem: () => stored,
    setItem: (_key: string, value: string) => {
      stored = value;
    },
    removeItem: () => {
      stored = null;
    },
    read() {
      return stored ? (JSON.parse(stored) as PrototypeState) : null;
    },
  };
}

function RapidActionsProbe() {
  const { confirmAlert, createAlert, respondToAlert, selectUser, state } = usePrototype();
  const customAlert = state.alerts.find((alert) => alert.patientReference === draftInput.patientReference);
  const marcRecipient = selectAlertById(state, "alert-critical-1")?.recipients.find(
    (recipient) => recipient.clinicianId === "clinician-marc",
  );

  return (
    <div>
      <p>custom-alert:{customAlert ? `${customAlert.id}:${customAlert.status}` : "missing"}</p>
      <p>selected-user:{state.selectedUserId}</p>
      <p>marc-response:{marcRecipient?.response ?? "missing"}</p>
      <button
        type="button"
        onClick={() => {
          const alertId = createAlert(draftInput);
          confirmAlert(alertId);
        }}
      >
        Create and confirm
      </button>
      <button
        type="button"
        onClick={() => {
          selectUser("user-marc");
          respondToAlert("alert-critical-1", "clinician-marc", "acknowledged", "SIMULATION: rapid response.");
        }}
      >
        Switch and respond
      </button>
    </div>
  );
}

describe("prototype alert store", () => {
  it("exports PrototypeAction for downstream typed consumers", () => {
    const action: PrototypeAction = {
      type: "user-selected",
      userId: "user-marc",
    };

    expect(prototypeReducer(createSeedState(), action).selectedUserId).toBe("user-marc");
  });

  it("creates one canonical draft and confirms it without implying delivery", () => {
    const initial = createSeedState();
    const created = prototypeReducer(initial, { type: "alert-created", alert: buildAlert(draftInput, "alert-new") });
    const confirmed = prototypeReducer(created, { type: "alert-confirmed", alertId: "alert-new", occurredAt: DEMO_NOW });
    const alert = selectAlertById(confirmed, "alert-new");

    expect(alert?.status).toBe("sent");
    expect(alert?.deliveryState).toBe("not-observed");
    expect(alert?.recipients[0].response).toBe("none");
  });

  it("records acknowledgement separately from responsibility acceptance", () => {
    const state = prototypeReducer(createSeedState(), {
      type: "doctor-responded",
      alertId: "alert-critical-1",
      clinicianId: "clinician-marc",
      response: "acknowledged",
      note: "SIMULATION: received.",
      occurredAt: DEMO_NOW,
    });

    const recipient = selectAlertById(state, "alert-critical-1")?.recipients[0];
    expect(recipient?.response).toBe("acknowledged");
    expect(recipient?.responsibilityAcceptedAt).toBeUndefined();
  });

  it("rejects incompatible stored state and restores deterministic seed data", () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ schemaVersion: 999 }));
    expect(loadPrototypeState()).toEqual(createSeedState());
  });

  it("persists the selected user in compatible stored state", () => {
    const selected = prototypeReducer(createSeedState(), {
      type: "user-selected",
      userId: "user-marc",
    });

    savePrototypeState(selected);

    expect(loadPrototypeState().selectedUserId).toBe("user-marc");
  });

  it("restores the deterministic seed when the demo resets", () => {
    const modified = prototypeReducer(createSeedState(), {
      type: "user-selected",
      userId: "user-marc",
    });

    expect(prototypeReducer(modified, { type: "demo-reset" })).toEqual(createSeedState());
  });

  it.each([
    ["declined", "warning"],
    ["unavailable", "critical"],
  ] satisfies Array<[Exclude<DoctorResponse, "none" | "acknowledged" | "accepted">, "warning" | "critical"]>)(
    "records %s responses without auto-escalating a sent alert",
    (response, tone) => {
      const state = prototypeReducer(createSeedState(), {
        type: "doctor-responded",
        alertId: "alert-critical-1",
        clinicianId: "clinician-marc",
        response,
        note: `SIMULATION: ${response}.`,
        occurredAt: DEMO_NOW,
      });

      const alert = selectAlertById(state, "alert-critical-1");
      const recipient = alert?.recipients[0];

      expect(alert?.status).toBe("sent");
      expect(recipient?.response).toBe(response);
      expect(recipient?.respondedAt).toBe(DEMO_NOW);
      expect(recipient?.responsibilityAcceptedAt).toBeUndefined();
      expect(alert?.activities[0]).toMatchObject({
        kind: response,
        tone,
      });
    },
  );

  it("seeds the resolved alert with distinct delivery and acceptance milestones", () => {
    const alert = selectAlertById(createSeedState(), "alert-resolved-1");

    expect(alert?.status).toBe("resolved");
    expect(alert?.deliveryState).toBe("delivered");
    expect(alert?.receivedAt).toBe("2026-08-30T12:39:00.000Z");
    expect(alert?.activities.map((activity) => activity.kind)).toEqual(["accepted", "acknowledged", "sent", "created"]);
    expect(alert?.recipients[0]).toMatchObject({
      response: "accepted",
      acknowledgedAt: "2026-08-30T12:40:00.000Z",
      responsibilityAcceptedAt: "2026-08-30T12:42:00.000Z",
    });
  });

  it("filters alerts by status, urgency, department, and updated time", () => {
    const baseState = createSeedState();
    const cancelledState: PrototypeState = {
      ...baseState,
      alerts: [
        {
          ...baseState.alerts[0],
          id: "alert-cancelled-1",
          status: "cancelled",
          updatedAt: "2026-08-30T14:30:00.000Z",
        },
        ...baseState.alerts,
      ],
    };

    expect(selectAlerts(baseState, { urgency: "critical", department: "Fictional Emergency" }).map((alert) => alert.id)).toEqual([
      "alert-critical-1",
      "alert-in-progress-1",
      "alert-escalating-1",
    ]);
    expect(selectAlerts(cancelledState, { status: "cancelled" }).map((alert) => alert.id)).toEqual(["alert-cancelled-1"]);
    expect(selectAlerts(baseState, { updatedAfter: "2026-08-30T14:10:00.000Z" }).map((alert) => alert.id)).toEqual([
      "alert-critical-1",
      "alert-in-progress-1",
      "alert-escalating-1",
    ]);
  });

  it("selects doctor inbox tabs from the seeded Marc assignment mix", () => {
    const state = createSeedState();

    expect(selectDoctorAlerts(state, "clinician-marc", "all").map((alert) => alert.id)).toEqual([
      "alert-critical-1",
      "alert-in-progress-1",
      "alert-escalating-1",
    ]);
    expect(selectDoctorAlerts(state, "clinician-marc", "unread").map((alert) => alert.id)).toEqual(["alert-critical-1"]);
    expect(selectDoctorAlerts(state, "clinician-marc", "in-progress").map((alert) => alert.id)).toEqual([
      "alert-in-progress-1",
      "alert-escalating-1",
    ]);
    expect(selectDoctorAlerts(state, "clinician-marc", "completed").map((alert) => alert.id)).toEqual([
      "alert-escalating-1",
    ]);
  });

  it("searches clinicians across names, specialties, departments, sites, and initials", () => {
    const state = createSeedState();

    expect(searchClinicians(state, "cardiology").map((clinician) => clinician.id)).toEqual(["clinician-julie"]);
    expect(searchClinicians(state, "south wing").map((clinician) => clinician.id)).toEqual(["clinician-david"]);
    expect(searchClinicians(state, "mt").map((clinician) => clinician.id)).toEqual(["clinician-marc"]);
  });

  it("caps stored response notes at 500 characters", () => {
    const note = `SIMULATION:${"x".repeat(510)}`;
    const state = prototypeReducer(createSeedState(), {
      type: "doctor-responded",
      alertId: "alert-critical-1",
      clinicianId: "clinician-marc",
      response: "accepted",
      note,
      occurredAt: DEMO_NOW,
    });

    expect(selectAlertById(state, "alert-critical-1")?.recipients[0].note).toHaveLength(500);
  });

  it("ignores doctor responses when the alert or clinician recipient is absent", () => {
    const initial = createSeedState();
    const unchangedForMissingAlert = prototypeReducer(initial, {
      type: "doctor-responded",
      alertId: "missing-alert",
      clinicianId: "clinician-marc",
      response: "accepted",
      note: "SIMULATION: should not be recorded.",
      occurredAt: DEMO_NOW,
    });
    const unchangedForMissingRecipient = prototypeReducer(initial, {
      type: "doctor-responded",
      alertId: "alert-in-progress-1",
      clinicianId: "clinician-julie",
      response: "accepted",
      note: "SIMULATION: should not be recorded.",
      occurredAt: DEMO_NOW,
    });

    expect(unchangedForMissingAlert).toEqual(initial);
    expect(unchangedForMissingRecipient).toEqual(initial);
  });

  it("preserves in-memory state when storage writes fail and exposes a storage error", async () => {
    const storage = {
      getItem: () => null,
      setItem: () => {
        throw new Error("disk full");
      },
      removeItem: () => undefined,
    };
    const initialState: PrototypeState = createSeedState();

    renderPrototype(<PrototypeProbe />, { state: initialState, storage });

    fireEvent.click(screen.getByRole("button", { name: "Switch user" }));

    expect(await screen.findByText("selected-user:user-marc")).toBeVisible();
    expect(
      await screen.findByText(
        "storage-error:Demo changes are available for this session but could not be saved in this browser.",
      ),
    ).toBeVisible();
  });

  it("applies rapid sequential commands to the latest in-memory state and localStorage", async () => {
    const storage = createMemoryStorage(createSeedState());

    renderPrototype(<RapidActionsProbe />, { state: createSeedState(), storage });

    fireEvent.click(screen.getByRole("button", { name: "Create and confirm" }));

    await screen.findByText(/^custom-alert:alert-custom-6:sent$/);
    expect(storage.read()?.alerts.find((alert) => alert.patientReference === draftInput.patientReference)?.status).toBe(
      "sent",
    );

    fireEvent.click(screen.getByRole("button", { name: "Switch and respond" }));

    await screen.findByText("selected-user:user-marc");
    await screen.findByText("marc-response:acknowledged");

    const saved = storage.read();
    expect(saved?.selectedUserId).toBe("user-marc");
    expect(
      saved?.alerts
        .find((alert) => alert.id === "alert-critical-1")
        ?.recipients.find((recipient) => recipient.clinicianId === "clinician-marc")?.response,
    ).toBe("acknowledged");
  });

  it("creates unique same-time response activities in action order", () => {
    const afterMarc = prototypeReducer(createSeedState(), {
      type: "doctor-responded",
      alertId: "alert-critical-1",
      clinicianId: "clinician-marc",
      response: "acknowledged",
      note: "SIMULATION: first same-time response.",
      occurredAt: DEMO_NOW,
    });
    const afterJulie = prototypeReducer(afterMarc, {
      type: "doctor-responded",
      alertId: "alert-critical-1",
      clinicianId: "clinician-julie",
      response: "acknowledged",
      note: "SIMULATION: second same-time response.",
      occurredAt: DEMO_NOW,
    });

    const acknowledgements =
      selectAlertById(afterJulie, "alert-critical-1")?.activities.filter((activity) => activity.kind === "acknowledged") ??
      [];

    expect(new Set(acknowledgements.map((activity) => activity.id))).toHaveProperty("size", 2);
    expect(acknowledgements.map((activity) => activity.id)).toEqual([
      expect.stringContaining("clinician-marc"),
      expect.stringContaining("clinician-julie"),
    ]);
  });
});
