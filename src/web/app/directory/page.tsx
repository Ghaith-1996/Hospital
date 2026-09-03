import { PageHeader } from "../../components/ui/page-header";
import { ScreenState } from "../../components/ui/screen-state";

export default function DirectoryPage() {
  return (
    <>
      <PageHeader
        title="Directory"
        description="The fictional directory workspace is scheduled for a later prototype task."
      />
      <ScreenState
        kind="empty"
        label="Directory is coming later"
        description="Use Alert Doctor or Alerts in this frontend-only prototype shell."
        headingLevel="h2"
      />
    </>
  );
}
