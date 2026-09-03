"use client";

import React from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { ScreenState } from "../../../components/ui/screen-state";
import { StatusBadge } from "../../../components/ui/status-badge";
import { selectAlertById } from "../../../features/alerts/selectors";
import { usePrototype } from "../../../features/alerts/prototype-store";

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

export default function AlertDetailsPage() {
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

  return (
    <section className="alert-details-placeholder" aria-labelledby="alert-details-heading">
      <Link className="focus-link" href="/alerts">
        Back to Alerts
      </Link>
      <div>
        <h1 id="alert-details-heading">Alert Details</h1>
        <p>
          This details view is a local transitional placeholder for the Task 5 sent action. The complete live details
          screen is scheduled for a later frontend prototype task.
        </p>
      </div>
      <dl className="review-details">
        <div>
          <dt>Patient Reference</dt>
          <dd>{alert.patientReference}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>
            <StatusBadge status={alert.status} />
          </dd>
        </div>
        <div>
          <dt>Delivery State</dt>
          <dd>{alert.deliveryState}</dd>
        </div>
        <div>
          <dt>Recipient Responses</dt>
          <dd>{alert.recipients.map((recipient) => recipient.response).join(", ")}</dd>
        </div>
      </dl>
    </section>
  );
}
