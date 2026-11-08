import type { ReactNode } from 'react';
import { useServerWake, type ServerWakeOptions } from '../api/serverWake';
import { messages } from '../i18n';

interface ServerWakeGateProps extends ServerWakeOptions {
  children: ReactNode;
}

const t = messages.serverWake;

/** Renders its children once the API answers; until then, the cold start message. Gives up after 90 s. */
export function ServerWakeGate({ children, ...options }: ServerWakeGateProps) {
  const { status, elapsedSeconds, retry } = useServerWake(options);

  switch (status) {
    case 'ready':
      return children;
    case 'checking':
      return null;
    case 'waking':
      return (
        <section className="card">
          <p role="status">{t.waking}</p>
          <p className="muted">{t.elapsed(elapsedSeconds)}</p>
        </section>
      );
    case 'unreachable':
      return (
        <section className="card">
          <p role="alert">{t.unreachable}</p>
          <button type="button" className="primary" onClick={retry}>
            {t.retry}
          </button>
        </section>
      );
  }
}
