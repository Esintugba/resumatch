import type { ReactNode } from 'react';
import { messages } from '../../i18n';
import styles from './Frame.module.css';

/** The page frame: skip link, header and main. Used inside and outside the router. */
export function Frame({ headerEnd, children }: { headerEnd?: ReactNode; children: ReactNode }) {
  return (
    <>
      <a className={styles.skipLink} href="#main">
        {messages.skipToContent}
      </a>
      <header className={styles.header}>
        <span className={styles.brand}>{messages.appName}</span>
        {headerEnd}
      </header>
      <main id="main" className={styles.main}>
        {children}
      </main>
    </>
  );
}
