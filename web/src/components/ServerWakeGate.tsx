import type { ReactNode } from 'react';
import { useServerWake, type ServerWakeOptions } from '../api/serverWake';
import { messages } from '../i18n';
import { Button } from './ui/Button';
import { Card } from './ui/Card';
import { Frame } from './ui/Frame';
import text from './ui/Text.module.css';

interface ServerWakeGateProps extends ServerWakeOptions {
  children: ReactNode;
}

const t = messages.serverWake;

/** Renders its children once the API answers; until then, the cold start message in the page frame. Gives up after 90 s. */
export function ServerWakeGate({ children, ...options }: ServerWakeGateProps) {
  const { status, elapsedSeconds, retry } = useServerWake(options);

  switch (status) {
    case 'ready':
      return children;
    case 'checking':
      return null;
    case 'waking':
      return (
        <Frame>
          <Card>
          <p role="status">{t.waking}</p>
          <p className={text.muted}>{t.elapsed(elapsedSeconds)}</p>
          </Card>
        </Frame>
      );
    case 'unreachable':
      return (
        <Frame>
          <Card>
          <p role="alert">{t.unreachable}</p>
          <Button tone="primary" onClick={retry}>
            {t.retry}
          </Button>
          </Card>
        </Frame>
      );
  }
}
