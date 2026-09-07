import { requestJson } from "./alerts";
export type DirectoryImportPreview = {
  sourceSystem: string; parsedPractitionerCount: number; insertCount: number; updateCount: number; rejectedCount: number;
  errors: Array<{ code: string; rowNumber: number | null; message: string }>;
  warnings: Array<{ code: string; rowNumber: number | null; message: string }>;
  changes: Array<{ action: string; simulationCode: string; displayName: string; selectable: boolean }>;
  previewToken: string;
};
export function previewDirectoryImport(file: File) {
  const body = new FormData(); body.append("file", file);
  return requestJson<DirectoryImportPreview>("/api/v1/directory/imports/preview", { method: "POST", body });
}
export function applyDirectoryImport(file: File, previewToken: string) {
  const body = new FormData(); body.append("file", file); body.append("preview_token", previewToken);
  return requestJson<{ applied: boolean; syncRunId: string | null; preview: DirectoryImportPreview }>("/api/v1/directory/imports", { method: "POST", body });
}
