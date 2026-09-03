"use client";

import React from "react";
import { useRouter } from "next/navigation";
import { ScreenState } from "../../../../components/ui/screen-state";

export default function AlertComposePage() {
  const router = useRouter();

  React.useEffect(() => {
    router.replace("/alerts/new");
  }, [router]);

  return (
    <ScreenState
      kind="loading"
      label="Returning to alert creation"
      description="This older compose route is not active in the frontend-only prototype shell."
    />
  );
}
