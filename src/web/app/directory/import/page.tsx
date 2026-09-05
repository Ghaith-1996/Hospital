import Link from "next/link";
import { ScreenState } from "../../../components/ui/screen-state";

export default function DirectoryImportPage() {
  return (
    <ScreenState
      kind="empty"
      label="Directory is coming later"
      description="The redesigned frontend is local-only. A future backend phase will reconnect fictional directory management."
      action={
        <Link className="focus-link" href="/alerts/new">
          Alert Doctor
        </Link>
      }
    />
  );
}
