import { useEffect, useId, useRef } from 'react';
import { Button } from './Button';
import styles from './ConfirmDialog.module.css';

interface ConfirmDialogProps {
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel: string;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * A modal confirmation for an action that loses data. Rendered only while
 * needed. Focus starts on the safe choice, and Escape cancels.
 */
export function ConfirmDialog({ title, description, confirmLabel, cancelLabel, onConfirm, onCancel }: ConfirmDialogProps) {
  const dialog = useRef<HTMLDialogElement>(null);
  const safeChoice = useRef<HTMLButtonElement>(null);
  const titleId = useId();
  const descriptionId = useId();

  useEffect(() => {
    const element = dialog.current;
    if (!element || element.open) return;
    // jsdom has no showModal; the open attribute is enough there.
    if (typeof element.showModal === 'function') element.showModal();
    else element.setAttribute('open', '');
    // After opening, so the browser does not move focus elsewhere.
    safeChoice.current?.focus();
  }, []);

  return (
    <dialog
      ref={dialog}
      role="alertdialog"
      aria-modal="true"
      aria-labelledby={titleId}
      aria-describedby={descriptionId}
      className={styles.dialog}
      onCancel={(event) => {
        event.preventDefault();
        onCancel();
      }}
    >
      <h2 id={titleId} className={styles.title}>
        {title}
      </h2>
      <p id={descriptionId} className={styles.description}>
        {description}
      </p>
      <div className={styles.actions}>
        <Button ref={safeChoice} tone="secondary" onClick={onCancel}>
          {cancelLabel}
        </Button>
        <Button tone="danger" onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </dialog>
  );
}
