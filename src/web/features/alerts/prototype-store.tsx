import React from "react";
import { createSeedState, DEMO_NOW, PROTOTYPE_SCHEMA_VERSION, STORAGE_KEY } from "./seed";
import type {
  AlertActivity,
  AlertRecord,
  AlertStatus,
  DoctorResponse,
  NewAlertInput,
  PrototypeState,
} from "./types";

export type PrototypeAction =
  | { type: "user-selected"; userId: string }
  | { type: "alert-created"; alert: AlertRecord }
  | { type: "alert-updated"; alertId: string; input: NewAlertInput }
  | { type: "alert-confirmed"; alertId: string; occurredAt: string }
  | {
      type: "doctor-responded";
      alertId: string;
      clinicianId: string;
      response: Exclude<DoctorResponse, "none">;
      note: string;
      occurredAt: string;
    }
  | { type: "demo-reset" };

const STORAGE_ERROR_MESSAGE = "Demo changes are available for this session but could not be saved in this browser.";
const MAX_NOTE_LENGTH = 500;

function createRecipients(clinicianIds: string[]) {
  return clinicianIds.map((clinicianId) => ({
    clinicianId,
    response: "none" as const,
  }));
}

function buildLabel(caseDetails: string): string {
  const firstSentence = caseDetails.trim().match(/[^.!?]+[.!?]?/u)?.[0]?.trim() ?? "SIMULATION: fictional alert.";
  return firstSentence.slice(0, 64).trimEnd();
}

function createActivity(
  alertId: string,
  kind: AlertActivity["kind"],
  actorId: string,
  sequence: number,
  label: string,
  occurredAt: string,
  tone: AlertActivity["tone"],
): AlertActivity {
  return {
    id: `activity-${alertId}-${kind}-${actorId}-seq${String(sequence).padStart(3, "0")}-${occurredAt}`,
    kind,
    label,
    occurredAt,
    tone,
  };
}

function readActivitySequence(activity: AlertActivity) {
  const parsed = activity.id.match(/-seq(\d+)-/u)?.[1];
  return parsed ? Number(parsed) : 0;
}

function nextActivitySequence(alert: AlertRecord) {
  return Math.max(0, ...alert.activities.map(readActivitySequence)) + 1;
}

function compareActivitiesNewestFirst(left: AlertActivity, right: AlertActivity) {
  const timeComparison = right.occurredAt.localeCompare(left.occurredAt);
  if (timeComparison !== 0) return timeComparison;
  return readActivitySequence(left) - readActivitySequence(right);
}

function addActivity(alert: AlertRecord, activity: AlertActivity) {
  return [...alert.activities, activity].sort(compareActivitiesNewestFirst);
}

function resolveStorage(storage?: Pick<Storage, "getItem" | "setItem" | "removeItem">): Pick<
  Storage,
  "getItem" | "setItem" | "removeItem"
> | null {
  if (storage) return storage;
  if (typeof window === "undefined") return null;
  return window.localStorage;
}

function isPrototypeState(value: unknown): value is PrototypeState {
  if (!value || typeof value !== "object") return false;
  const candidate = value as Partial<PrototypeState>;
  return (
    candidate.schemaVersion === PROTOTYPE_SCHEMA_VERSION &&
    typeof candidate.selectedUserId === "string" &&
    Array.isArray(candidate.users) &&
    Array.isArray(candidate.clinicians) &&
    Array.isArray(candidate.alerts)
  );
}

function deriveStatusFromResponse(response: Exclude<DoctorResponse, "none">, currentStatus: AlertStatus) {
  if (response === "accepted") return "in-progress";
  return currentStatus;
}

function trimNote(note: string): string {
  return note.trim().slice(0, MAX_NOTE_LENGTH);
}

export function buildAlert(input: NewAlertInput, id: string): AlertRecord {
  return {
    id,
    label: buildLabel(input.caseDetails),
    patientReference: input.patientReference,
    location: input.location,
    department: input.department,
    urgency: input.urgency,
    caseDetails: input.caseDetails,
    status: "draft",
    deliveryState: "not-observed",
    createdByUserId: "user-sophie",
    createdAt: DEMO_NOW,
    updatedAt: DEMO_NOW,
    recipients: createRecipients(input.clinicianIds),
    activities: [
      createActivity(id, "created", "user-sophie", 1, "SIMULATION: fictional operator draft created.", DEMO_NOW, "neutral"),
    ],
  };
}

export function loadPrototypeState(storage?: Pick<Storage, "getItem">): PrototypeState {
  const resolvedStorage = storage ?? (typeof window !== "undefined" ? window.localStorage : undefined);
  if (!resolvedStorage) return createSeedState();

  try {
    const raw = resolvedStorage.getItem(STORAGE_KEY);
    if (!raw) return createSeedState();
    const parsed: unknown = JSON.parse(raw);
    if (!isPrototypeState(parsed)) return createSeedState();
    return parsed;
  } catch {
    return createSeedState();
  }
}

export function savePrototypeState(state: PrototypeState, storage?: Pick<Storage, "setItem">): void {
  const resolvedStorage = storage ?? (typeof window !== "undefined" ? window.localStorage : undefined);
  if (!resolvedStorage) return;
  resolvedStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

export function prototypeReducer(state: PrototypeState, action: PrototypeAction): PrototypeState {
  if (action.type === "user-selected") {
    return {
      ...state,
      selectedUserId: action.userId,
    };
  }

  if (action.type === "alert-created") {
    return {
      ...state,
      alerts: [action.alert, ...state.alerts],
    };
  }

  if (action.type === "alert-updated") {
    return {
      ...state,
      alerts: state.alerts.map((alert) =>
        alert.id !== action.alertId
          ? alert
          : {
              ...alert,
              label: buildLabel(action.input.caseDetails),
              patientReference: action.input.patientReference,
              location: action.input.location,
              department: action.input.department,
              urgency: action.input.urgency,
              caseDetails: action.input.caseDetails,
              recipients: createRecipients(action.input.clinicianIds),
              updatedAt: DEMO_NOW,
            },
      ),
    };
  }

  if (action.type === "alert-confirmed") {
    return {
      ...state,
      alerts: state.alerts.map((alert) =>
        alert.id !== action.alertId
          ? alert
          : {
              ...alert,
              status: "sent",
              updatedAt: action.occurredAt,
              activities: addActivity(
                alert,
                createActivity(
                  alert.id,
                  "sent",
                  "user-sophie",
                  nextActivitySequence(alert),
                  "SIMULATION: fictional alert confirmed without delivery observation.",
                  action.occurredAt,
                  "info",
                ),
              ),
            },
      ),
    };
  }

  if (action.type === "doctor-responded") {
    const targetAlert = state.alerts.find((alert) => alert.id === action.alertId);
    const targetRecipient = targetAlert?.recipients.find((recipient) => recipient.clinicianId === action.clinicianId);

    if (!targetAlert || !targetRecipient) {
      return state;
    }

    return {
      ...state,
      alerts: state.alerts.map((alert) => {
        if (alert.id !== action.alertId) return alert;

        return {
          ...alert,
          status: deriveStatusFromResponse(action.response, alert.status),
          updatedAt: action.occurredAt,
          recipients: alert.recipients.map((recipient) => {
            if (recipient.clinicianId !== action.clinicianId) return recipient;

            return {
              ...recipient,
              response: action.response,
              respondedAt: action.occurredAt,
              acknowledgedAt: action.response === "acknowledged" || action.response === "accepted" ? action.occurredAt : recipient.acknowledgedAt,
              responsibilityAcceptedAt: action.response === "accepted" ? action.occurredAt : undefined,
              note: trimNote(action.note),
            };
          }),
          activities: addActivity(
            alert,
            createActivity(
              alert.id,
              action.response,
              action.clinicianId,
              nextActivitySequence(alert),
              `SIMULATION: fictional ${action.response} response recorded.`,
              action.occurredAt,
              action.response === "accepted" ? "success" : action.response === "acknowledged" ? "info" : action.response === "declined" ? "warning" : "critical",
            ),
          ),
        };
      }),
    };
  }

  if (action.type === "demo-reset") {
    return createSeedState();
  }

  return state;
}

export type PrototypeContextValue = {
  state: PrototypeState;
  hydrated: boolean;
  resetGeneration: number;
  storageError: string | null;
  selectUser(userId: string): void;
  createAlert(input: NewAlertInput): string;
  updateAlert(alertId: string, input: NewAlertInput): void;
  confirmAlert(alertId: string): void;
  respondToAlert(alertId: string, clinicianId: string, response: Exclude<DoctorResponse, "none">, note: string): void;
  resetDemo(): void;
};

const PrototypeContext = React.createContext<PrototypeContextValue | null>(null);

type ProviderState = {
  prototype: PrototypeState;
  hydrated: boolean;
  storageError: string | null;
  resetGeneration: number;
};

type ProviderAction =
  | { type: "hydrate"; state: PrototypeState }
  | { type: "persist-succeeded" }
  | { type: "persist-failed" }
  | { type: "prototype-replaced"; state: PrototypeState; resetGeneration?: number }
  | { type: "prototype"; action: PrototypeAction };

function providerReducer(state: ProviderState, action: ProviderAction): ProviderState {
  if (action.type === "hydrate") {
    return {
      prototype: action.state,
      hydrated: true,
      storageError: null,
      resetGeneration: state.resetGeneration,
    };
  }

  if (action.type === "persist-succeeded") {
    if (state.storageError === null) return state;
    return {
      ...state,
      storageError: null,
    };
  }

  if (action.type === "persist-failed") {
    if (state.storageError === STORAGE_ERROR_MESSAGE) return state;
    return {
      ...state,
      storageError: STORAGE_ERROR_MESSAGE,
    };
  }

  if (action.type === "prototype-replaced") {
    return {
      ...state,
      prototype: action.state,
      resetGeneration: action.resetGeneration ?? state.resetGeneration,
    };
  }

  return {
    ...state,
    prototype: prototypeReducer(state.prototype, action.action),
  };
}

export function PrototypeProvider({
  children,
  initialState,
  storage,
}: React.PropsWithChildren<{
  initialState?: PrototypeState;
  storage?: Pick<Storage, "getItem" | "setItem" | "removeItem">;
}>) {
  const hasInitialState = initialState !== undefined;
  const [providerState, dispatch] = React.useReducer(providerReducer, {
    prototype: initialState ?? createSeedState(),
    hydrated: hasInitialState,
    storageError: null,
    resetGeneration: 0,
  });
  const storageRef = React.useRef(resolveStorage(storage));
  const prototypeRef = React.useRef(providerState.prototype);

  React.useEffect(() => {
    if (hasInitialState) return;
    const loaded = loadPrototypeState(storageRef.current ?? undefined);
    prototypeRef.current = loaded;
    dispatch({ type: "hydrate", state: loaded });
  }, [hasInitialState]);

  React.useEffect(() => {
    prototypeRef.current = providerState.prototype;
  }, [providerState.prototype]);

  React.useEffect(() => {
    if (!providerState.hydrated) return;
    try {
      savePrototypeState(providerState.prototype, storageRef.current ?? undefined);
      dispatch({ type: "persist-succeeded" });
    } catch {
      dispatch({ type: "persist-failed" });
    }
  }, [providerState.hydrated, providerState.prototype]);

  const applyPrototypeAction = React.useCallback(
    (action: PrototypeAction) => {
      const nextState = prototypeReducer(prototypeRef.current, action);
      prototypeRef.current = nextState;
      try {
        savePrototypeState(nextState, storageRef.current ?? undefined);
        dispatch({ type: "persist-succeeded" });
      } catch {
        dispatch({ type: "persist-failed" });
      }
      dispatch({
        type: "prototype-replaced",
        state: nextState,
        resetGeneration: action.type === "demo-reset" ? providerState.resetGeneration + 1 : undefined,
      });
      return nextState;
    },
    [providerState.resetGeneration],
  );

  const value = React.useMemo<PrototypeContextValue>(
    () => ({
      state: providerState.prototype,
      hydrated: providerState.hydrated,
      resetGeneration: providerState.resetGeneration,
      storageError: providerState.storageError,
      selectUser(userId: string) {
        applyPrototypeAction({ type: "user-selected", userId });
      },
      createAlert(input: NewAlertInput) {
        const id = `alert-custom-${prototypeRef.current.alerts.length + 1}`;
        applyPrototypeAction({ type: "alert-created", alert: buildAlert(input, id) });
        return id;
      },
      updateAlert(alertId: string, input: NewAlertInput) {
        applyPrototypeAction({ type: "alert-updated", alertId, input });
      },
      confirmAlert(alertId: string) {
        applyPrototypeAction({ type: "alert-confirmed", alertId, occurredAt: DEMO_NOW });
      },
      respondToAlert(alertId: string, clinicianId: string, response: Exclude<DoctorResponse, "none">, note: string) {
        applyPrototypeAction({ type: "doctor-responded", alertId, clinicianId, response, note, occurredAt: DEMO_NOW });
      },
      resetDemo() {
        try {
          storageRef.current?.removeItem(STORAGE_KEY);
        } catch {
          dispatch({ type: "persist-failed" });
        }
        applyPrototypeAction({ type: "demo-reset" });
      },
    }),
    [applyPrototypeAction, providerState],
  );

  return <PrototypeContext.Provider value={value}>{children}</PrototypeContext.Provider>;
}

export function usePrototype(): PrototypeContextValue {
  const context = React.useContext(PrototypeContext);
  if (!context) {
    throw new Error("usePrototype must be used within PrototypeProvider.");
  }
  return context;
}
