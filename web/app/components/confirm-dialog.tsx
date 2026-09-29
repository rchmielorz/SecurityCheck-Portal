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
  // Where focus goes after a confirm. The invoker is often disabled or removed by
  // the confirmed action, so focus on it would be lost; without this, focus returns
  // to the invoker on confirm too.
  focusAfterConfirm?: () => HTMLElement | null | undefined;
  // Element to return focus to on cancel. Pass the clicked button: a mouse click does not
  // focus buttons in Safari/Firefox, so document.activeElement is unreliable. Read when the dialog opens.
  invoker?: HTMLElement | null;
};

// Controlled native <dialog>. Esc (native `cancel` event) calls onCancel; the
// parent must set `open` to false in onConfirm/onCancel. On cancel, focus returns
// to the element that was focused when the dialog opened.
export function ConfirmDialog({
  open,
  title,
  description,
  confirmLabel = "Potwierdź",
  cancelLabel = "Anuluj",
  onConfirm,
  onCancel,
  focusAfterConfirm,
  invoker: invokerProp,
}: ConfirmDialogProps) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  const confirmedRef = useRef(false);
  const focusAfterConfirmRef = useRef(focusAfterConfirm);
  focusAfterConfirmRef.current = focusAfterConfirm;

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog || !open) return;
    const invoker = invokerProp ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    confirmedRef.current = false;
    if (!dialog.open) dialog.showModal();
    return () => {
      if (dialog.open) dialog.close();
      const target = confirmedRef.current ? focusAfterConfirmRef.current?.() : undefined;
      // The native dialog usually restores focus itself; do it explicitly as a fallback.
      const next = target ?? invoker;
      if (next?.isConnected) next.focus();
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
        <Button
          variant="danger"
          onClick={() => {
            confirmedRef.current = true;
            onConfirm();
          }}
        >
          {confirmLabel}
        </Button>
      </div>
    </dialog>
  );
}
