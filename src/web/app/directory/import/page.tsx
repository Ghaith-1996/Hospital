import { PageHeader } from "../../../components/ui/page-header";
import { ScreenState } from "../../../components/ui/screen-state";

export default function DirectoryImportPage() {
  return (
    <>
      <PageHeader
        title="Directory Import"
        description="CSV import is not connected in this frontend-only prototype shell."
      />
      <ScreenState
        kind="empty"
        label="Directory import is coming later"
        description="No file is uploaded, parsed, or sent to a backend from this prototype route."
        headingLevel="h2"
      />
    </>
  );
}
