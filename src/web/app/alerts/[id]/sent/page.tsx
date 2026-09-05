"use client";

import React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ProgressSteps } from "../../../../components/alerts/progress-steps";
import { CheckIcon } from "../../../../components/ui/icons";
import { ScreenState } from "../../../../components/ui/screen-state";
import { selectAlertById } from "../../../../features/alerts/selectors";
import { usePrototype } from "../../../../features/alerts/prototype-store";

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function AlertSentPage() {
  const params = useParams<{ id?: string | string[] }>();
  const alertId = readRouteId(params.id);
  const { state } = usePrototype();
  const alert = alertId ? selectAlertById(state, alertId) : undefined;

  if (!alertId || !alert) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This local prototype could not find that fictional alert."
        action={
          <Link className="focus-link" href="/alerts/new">
            Create another alert
          </Link>
        }
      />
    );
  }

  const recipientCount = alert.recipients.length;

  return (
    <div className="sent-page">
      <ProgressSteps current={3} />

      <section className="sent-success" aria-labelledby="sent-success-heading">
        <span className="sent-success__icon" aria-hidden="true">
          <CheckIcon />
        </span>
        <h1 id="sent-success-heading">Alert Sent Successfully!</h1>
        <p>
          This local prototype simulated sending to {recipientCount} fictional clinician
          {recipientCount === 1 ? "" : "s"}. No real notification was sent.
        </p>
        <Link className="button-primary" href={`/alerts/${alert.id}`}>
          View Alert Details
        </Link>
      </section>

      <section className="sent-next-panel" aria-labelledby="sent-next-heading">
        <h2 id="sent-next-heading">What happens next?</h2>
        <ul>
          <li>Recipients can review the fictional alert in the prototype inbox.</li>
          <li>You can inspect the local alert record from the alert details page.</li>
          <li>Delivery, opened, acknowledged, and accepted stay separate local states.</li>
        </ul>
      </section>

      <div className="sent-page__actions">
        <Link className="focus-link" href="/alerts/new">
          Create Another Alert
        </Link>
      </div>
    </div>
  );
}
