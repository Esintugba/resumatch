import { forwardRef, type ReactNode } from 'react';
import styles from './Alert.module.css';
import { cx } from './cx';

interface AlertProps {
  tone: 'error' | 'warning' | 'success';
  /** alert interrupts (errors); status waits for the screen reader to finish. */
  role: 'alert' | 'status';
  title?: string;
  children?: ReactNode;
  actions?: ReactNode;
}

export const Alert = forwardRef<HTMLDivElement, AlertProps>(function Alert(
  { tone, role, title, children, actions },
  ref,
) {
  return (
    <div ref={ref} role={role} tabIndex={-1} className={cx(styles.alert, styles[tone])}>
      {title && <p className={styles.title}>{title}</p>}
      {children}
      {actions && <div className={styles.actions}>{actions}</div>}
    </div>
  );
});
