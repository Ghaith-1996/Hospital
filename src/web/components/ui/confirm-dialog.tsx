import React from "react";

export type ConfirmDialogProps = {
  open: boolean;
  recipientNames: string[];
  onCancel(): void;
  onConfirm(): void;
};

const focusableSelector = [
  "button:not([disabled])",
  "[href]",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

export function ConfirmDialog({ open, recipientNames, onCancel, onConfirm }: ConfirmDialogProps) {
  const titleId = React.useId();
  const dialogRef = React.useRef<HTMLDivElement>(null);
  const cancelRef = React.useRef<HTMLButtonElement>(null);
  const previousFocusRef = React.useRef<HTMLElement | null>(null);

  React.useEffect(() => {
    if (!open) return;

    previousFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    cancelRef.current?.focus();

    return () => {
      previousFocusRef.current?.focus();
    };
  }, [open]);

  if (!open) return null;

  function handleKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (event.key === "Escape") {
      event.preventDefault();
      onCancel();
      return;
    }

    if (event.key !== "Tab") return;

    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(focusableSelector) ?? []);
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
      return;
    }

    if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  const recipientCount = recipientNames.length;
  const names = recipientNames.join(", ");

  return (
    <div className="confirm-dialog" data-testid="confirm-dialog-backdrop">
      <div
        className="confirm-dialog__panel"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        ref={dialogRef}
        onKeyDown={handleKeyDown}
      >
        <h2 id={titleId}>Confirm alert dispatch?</h2>
        <p>
          You are about to send this fictional alert to {recipientCount} clinician{recipientCount === 1 ? "" : "s"}:
          {" "}
          {names}.
        </p>
        <p>
          This is a local simulation only. It will not contact real clinicians, observe delivery, or start an
          escalation.
        </p>
        <div className="confirm-dialog__actions">
          <button type="button" className="button-secondary" onClick={onCancel} ref={cancelRef}>
            Cancel
          </button>
          <button type="button" onClick={onConfirm}>
            Confirm fictional dispatch
          </button>
        </div>
      </div>
    </div>
  );
}
