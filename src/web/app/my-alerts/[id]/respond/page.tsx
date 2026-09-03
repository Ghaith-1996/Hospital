"use client";

import React from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import { PageHeader } from "../../../../components/ui/page-header";
import { ScreenState } from "../../../../components/ui/screen-state";
import { formatAlertDisplayTitle, selectAlertById, selectCurrentUser } from "../../../../features/alerts/selectors";
import { usePrototype } from "../../../../features/alerts/prototype-store";
import type { AlertRecord, DoctorResponse } from "../../../../features/alerts/types";

type ResponseChoice = Exclude<DoctorResponse, "none">;

const responseChoices: Array<{ value: ResponseChoice; label: string; description: string }> = [
  { value: "acknowledged", label: "Acknowledge", description: "I have received this alert." },
  { value: "accepted", label: "Accept", description: "I will take responsibility for this fictional case." },
  { value: "declined", label: "Decline", description: "I am not able to take this fictional case." },
  { value: "unavailable", label: "Unavailable", description: "I am currently unavailable." },
];

const queryResponses: Record<string, ResponseChoice> = {
  acknowledged: "acknowledged",
  accepted: "accepted",
  declined: "declined",
  unavailable: "unavailable",
};

function readRouteId(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function alertTitle(alert: AlertRecord) {
  return formatAlertDisplayTitle(alert);
}

function initialResponse(queryValue: string | null): ResponseChoice {
  return queryValue && queryValue in queryResponses ? queryResponses[queryValue] : "acknowledged";
}

export default function RespondToAlertPage() {
  const params = useParams<{ id?: string | string[] }>();
  const searchParams = useSearchParams();
  const router = useRouter();
  const alertId = readRouteId(params.id);
  const { state, respondToAlert } = usePrototype();
  const currentUser = selectCurrentUser(state);
  const alert = alertId ? selectAlertById(state, alertId) : undefined;
  const [selectedResponse, setSelectedResponse] = React.useState<ResponseChoice>(() =>
    initialResponse(searchParams.get("response")),
  );
  const [note, setNote] = React.useState("");

  if (!alertId || !alert || currentUser.role !== "doctor" || !currentUser.clinicianId) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This local prototype could not find that fictional doctor alert."
        action={
          <Link className="focus-link" href="/my-alerts">
            Back to Inbox
          </Link>
        }
      />
    );
  }

  const resolvedAlertId = alertId;
  const clinicianId = currentUser.clinicianId;
  const currentRecipient = alert.recipients.find((recipient) => recipient.clinicianId === clinicianId);

  if (!currentRecipient) {
    return (
      <ScreenState
        kind="not-found"
        label="Fictional alert not found"
        description="This fictional alert is not assigned to the selected doctor."
        action={
          <Link className="focus-link" href="/my-alerts">
            Back to Inbox
          </Link>
        }
      />
    );
  }

  function submitResponse(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    respondToAlert(resolvedAlertId, clinicianId, selectedResponse, note);
    router.push(`/my-alerts/${resolvedAlertId}?responded=1`);
  }

  return (
    <section className="respond-page">
      <Link className="focus-link" href={`/my-alerts/${resolvedAlertId}`}>
        Back to Alert
      </Link>
      <PageHeader title="Respond to Alert" description={alertTitle(alert)} />

      <form className="respond-form" onSubmit={submitResponse}>
        <fieldset className="response-options">
          <legend>Your Response</legend>
          {responseChoices.map((choice) => (
            <label className="response-option" key={choice.value}>
              <input
                aria-labelledby={`response-choice-${choice.value}`}
                type="radio"
                name="response"
                value={choice.value}
                checked={selectedResponse === choice.value}
                onChange={() => setSelectedResponse(choice.value)}
              />
              <span>
                <strong id={`response-choice-${choice.value}`}>{choice.label}</strong>
                <span>{choice.description}</span>
              </span>
            </label>
          ))}
        </fieldset>

        <div className="response-note">
          <label htmlFor="response-note">Add a Note (optional)</label>
          <textarea
            id="response-note"
            value={note}
            maxLength={500}
            placeholder="Add any notes or instructions..."
            onChange={(event) => setNote(event.target.value.slice(0, 500))}
          />
          <p className="character-counter" aria-live="polite">
            {note.length} / 500 characters
          </p>
        </div>

        <div className="respond-form__actions">
          <Link className="button-secondary" href={`/my-alerts/${resolvedAlertId}`}>
            Cancel
          </Link>
          <button className="button-primary" type="submit">
            Submit Response
          </button>
        </div>
      </form>
    </section>
  );
}
