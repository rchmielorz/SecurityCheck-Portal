import { useEffect, useId, useRef } from "react";

import { Button } from "./button";

type ConfirmDialogProps = {
  open: boolean;
  title: string;
  description?: string;
  confirmLabel?: string;
  cancelLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
};

// Controlled native <dialog>. Esc (native `cancel` event) calls onCancel; the
// parent must set `open` to false in onConfirm/onCancel. Focus returns to the
// element that was focused when the dialog opened.
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Potwierdź",
  cancelLabel = "Anuluj",
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open) return;
    const invoker = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    if (!dialog.open) dialog.showModal();
    return () => {
      if (dialog.open) dialog.close();
      // The native dialog usually restores focus itself; do it explicitly as a fallback.
      if (invoker?.isConnected) invoker.focus();
    };
  }, [open]);

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      aria-describedby={description ? descriptionId : undefined}
      onCancel={(event) => {
        event.preventDefault();
        onCancel();
      }}
      className="m-auto w-full max-w-md rounded-2xl border border-border-subtle bg-surface-raised p-6 text-text shadow-lg backdrop:bg-black/50"
    >
      <h2 id={titleId} className="text-lg font-semibold">
        {title}
      </h2>
      {description && (
        <p id={descriptionId} className="mt-2 text-sm text-text-muted">
          {description}
        </p>
      )}
      <div className="mt-6 flex justify-end gap-2">
        <Button variant="secondary" onClick={onCancel}>
          {cancelLabel}
        </Button>
        <Button variant="danger" onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </dialog>
  );
}
