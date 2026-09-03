"use client";

import React from "react";
import { useRouter } from "next/navigation";
import { ScreenState } from "../../../../components/ui/screen-state";

export default function AlertRecipientsPage() {
  const router = useRouter();

  React.useEffect(() => {
    router.replace("/alerts/new");
  }, [router]);

  return (
    <ScreenState
      kind="loading"
      label="Returning to alert creation"
      description="Recipient selection is scheduled for a later frontend-only prototype task."
    />
  );
}
