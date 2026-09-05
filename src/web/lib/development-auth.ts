import { requestJson } from "./alerts";

export type DevelopmentIdentity = {
  displayName: string;
  simulationHandle: string;
  roles: string[];
  organizationId: string;
};
export type CurrentUser = DevelopmentIdentity & { userId: string; developmentAuthentication: boolean };
export type SimulationLocationContext = {
  organizationId: string;
  sites: Array<{ siteId: string; name: string; departments: Array<{ departmentId: string; name: string }> }>;
};
export const getDevelopmentIdentities = () => requestJson<DevelopmentIdentity[]>("/api/v1/dev/identities");
export const getCurrentUser = () => requestJson<CurrentUser>("/api/v1/me");
export const createDevelopmentSession = (simulationHandle: string) => requestJson<void>("/api/v1/dev/session", {
  method: "POST", body: JSON.stringify({ simulationHandle }),
});
export const clearDevelopmentSession = () => requestJson<void>("/api/v1/dev/session/clear", { method: "POST" });
export const getSimulationLocationContext = () => requestJson<SimulationLocationContext>("/api/v1/dev/location-context");
