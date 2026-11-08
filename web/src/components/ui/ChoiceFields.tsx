import { useId, type ReactNode, type SelectHTMLAttributes } from 'react';
import type { UseFormRegisterReturn } from 'react-hook-form';
import { messages } from '../../i18n';
import styles from './ChoiceFields.module.css';

interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, keyof UseFormRegisterReturn> {
  label: string;
  registration: UseFormRegisterReturn;
  error?: string | undefined;
  children: ReactNode;
}

export function SelectField({ label, registration, error, children, ...rest }: SelectFieldProps) {
  const id = useId();
  return (
    <div className={styles.field}>
      <label htmlFor={id}>{label}</label>
      <select
        id={id}
        className={styles.select}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        {...rest}
        {...registration}
      >
        {children}
      </select>
      {error && (
        <p id={`${id}-error`} className={styles.error}>
          {error}
        </p>
      )}
    </div>
  );
}

export function CheckboxField({ label, registration }: { label: string; registration: UseFormRegisterReturn }) {
  const id = useId();
  return (
    <div className={styles.checkbox}>
      <input id={id} type="checkbox" {...registration} />
      <label htmlFor={id}>{label}</label>
    </div>
  );
}

interface MonthYearFieldProps {
  legend: string;
  month: UseFormRegisterReturn;
  year: UseFormRegisterReturn;
  error?: string | undefined;
  disabled?: boolean;
}

/**
 * A month and a year as a labelled group. Two plain fields rather than
 * type="month", which desktop Safari does not support.
 */
export function MonthYearField({ legend, month, year, error, disabled = false }: MonthYearFieldProps) {
  const id = useId();
  const errorId = `${id}-error`;
  const invalid = error ? true : undefined;
  const describedBy = error ? errorId : undefined;
  return (
    <fieldset className={styles.fieldset} aria-describedby={describedBy} disabled={disabled}>
      <legend className={styles.legend}>{legend}</legend>
      <div className={styles.monthYear}>
        <div>
          <label htmlFor={`${id}-month`}>{messages.dates.month}</label>
          <select id={`${id}-month`} className={styles.select} aria-invalid={invalid} aria-describedby={describedBy} {...month}>
            <option value="">{messages.dates.chooseMonth}</option>
            {messages.dates.months.map((name, index) => (
              <option key={name} value={String(index + 1).padStart(2, '0')}>
                {name}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label htmlFor={`${id}-year`}>{messages.dates.year}</label>
          <input
            id={`${id}-year`}
            className={styles.input}
            inputMode="numeric"
            autoComplete="off"
            maxLength={4}
            aria-invalid={invalid}
            aria-describedby={describedBy}
            {...year}
          />
        </div>
      </div>
      {error && (
        <p id={errorId} className={styles.error}>
          {error}
        </p>
      )}
    </fieldset>
  );
}
