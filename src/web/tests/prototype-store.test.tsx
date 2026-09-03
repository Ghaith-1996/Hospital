import React from "react";
import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { createSeedState, DEMO_NOW, STORAGE_KEY } from "../features/alerts/seed";
import {
  buildAlert,
  loadPrototypeState,
  prototypeReducer,
  savePrototypeState,
  usePrototype,
} from "../features/alerts/prototype-store";
import type { DoctorResponse, NewAlertInput, PrototypeState } from "../features/alerts/types";
import { selectAlertById } from "../features/alerts/selectors";
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

describe("prototype alert store", () => {
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
    "records %s responses with the expected recipient state and activity tone",
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

      expect(recipient?.response).toBe(response);
      expect(recipient?.respondedAt).toBe(DEMO_NOW);
      expect(recipient?.responsibilityAcceptedAt).toBeUndefined();
      expect(alert?.activities[0]).toMatchObject({
        kind: response,
        tone,
      });
    },
  );

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
});
