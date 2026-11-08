import { useId, type ReactNode } from 'react';
import { messages } from '../../../i18n';
import styles from './EditorSection.module.css';

interface EditorSectionProps {
  title: string;
  open: boolean;
  dirty: boolean;
  /** Asks to open or close; the editor decides, because another section may have unsaved changes. */
  onToggle: () => void;
  /** Shown while closed. */
  summary: ReactNode;
  /** The form, mounted only while open. */
  children: ReactNode;
}

/** One accordion item of the editor. Only one is open at a time. */
export function EditorSection({ title, open, dirty, onToggle, summary, children }: EditorSectionProps) {
  const id = useId();
  return (
    <section className={styles.section} aria-labelledby={`${id}-toggle`}>
      <h2 className={styles.heading}>
        <button
          id={`${id}-toggle`}
          type="button"
          className={styles.toggle}
          aria-expanded={open}
          aria-controls={`${id}-body`}
          onClick={onToggle}
        >
          {title}
          {open && dirty && <span className={styles.dirty}>{messages.unsaved.title}</span>}
          {/* Decoration only; kept out of the button's accessible name. */}
          <span aria-hidden="true" className={styles.chevron}>
            {open ? '▾' : '▸'}
          </span>
        </button>
      </h2>
      <div id={`${id}-body`} className={styles.body}>
        {open ? children : summary}
      </div>
    </section>
  );
}

/** The read-only view of a closed section. */
export function SectionSummary({ rows }: { rows: [label: string, value: string | null | undefined][] }) {
  // A section with nothing in it says so once, not once per field.
  if (rows.every(([, value]) => !value?.trim())) {
    return <p className={styles.nothing}>{messages.editor.nothingYet}</p>;
  }
  return (
    <dl className={styles.summary}>
      {rows.map(([label, value]) => (
        <div key={label} className={styles.row}>
          <dt>{label}</dt>
          <dd>{value?.trim() ? value : messages.common.empty}</dd>
        </div>
      ))}
    </dl>
  );
}

export function SaveBar({ children }: { children: ReactNode }) {
  return <div className={styles.saveBar}>{children}</div>;
}
