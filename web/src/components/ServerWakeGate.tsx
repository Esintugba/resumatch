import type { ReactNode } from 'react';
import { useServerWake, type ServerWakeOptions } from '../api/serverWake';

interface ServerWakeGateProps extends ServerWakeOptions {
  children: ReactNode;
}

/** Renders its children once the API answers; until then, the cold start message. Draft: texts move to i18n in PR 7. */
export function ServerWakeGate({ children, ...options }: ServerWakeGateProps) {
  const { status, elapsedSeconds, retry } = useServerWake(options);

  switch (status) {
    case 'ready':
      return children;
    case 'checking':
      return null;
    case 'waking':
      return (
        <section>
          <p role="status">Sunucu uyanıyor, en fazla bir dakika sürebilir.</p>
          <p>Geçen süre: {elapsedSeconds} sn</p>
        </section>
      );
    case 'unreachable':
      return (
        <section>
          <p role="alert">Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.</p>
          <button type="button" onClick={retry}>
            Tekrar dene
          </button>
        </section>
      );
  }
}
