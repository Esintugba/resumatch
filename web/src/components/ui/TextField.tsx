import { useId, type InputHTMLAttributes, type TextareaHTMLAttributes } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import styles from './TextField.module.css';

interface Common {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string | undefined;
  hint?: string;
}

type TextFieldProps = Common &
  (
    | ({ multiline?: false } & Omit<InputHTMLAttributes<HTMLInputElement>, keyof UseFormRegisterReturn>)
    | ({ multiline: true } & Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, keyof UseFormRegisterReturn>)
  );

/** A labelled input whose hint and error are tied to it with aria-describedby. */
export function TextField({ label, registration, error, hint, ...rest }: TextFieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [hint ? hintId : null, error ? errorId : null].filter(Boolean).join(' ') || undefined;
  const shared = {
    id,
    className: styles.input,
    'aria-invalid': error ? true : undefined,
    'aria-describedby': describedBy,
    ...registration,
  };

  return (
    <div className={styles.field}>
      <label htmlFor={id} className={styles.label}>
        {label}
      </label>
      {hint && (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      )}
      {rest.multiline ? (
        <textarea {...omitMultiline(rest)} {...shared} />
      ) : (
        <input {...omitMultiline(rest)} {...shared} />
      )}
      {error && (
        <p id={errorId} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  );
}

function omitMultiline<T extends { multiline?: boolean }>(props: T): Omit<T, 'multiline'> {
  const copy = { ...props };
  delete copy.multiline;
  return copy;
}
