"use client";

import React from "react";
import { ScreenState } from "../../../../components/ui/screen-state";

export default function AlertReviewPage() {
  return (
    <ScreenState
      kind="empty"
      label="Review step pending"
      description="This fictional local draft is ready for review. The full review and confirmation workflow is scheduled for the next frontend prototype task."
      action={
        <a className="focus-link" href="/alerts/new">
          Back to Alert Doctor
        </a>
      }
    />
  );
}
