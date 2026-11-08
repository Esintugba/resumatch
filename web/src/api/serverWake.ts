import { useCallback, useEffect, useState } from 'react';

// Draft of the cold start behaviour in docs/plans/frontend-roadmap.md:
// call /health/live on start; if it has not answered within 3 seconds, tell
// the user the server is waking up and show the elapsed time. Only this GET is
// retried, with exponential backoff, for at most 90 seconds.

export type ServerWakeStatus = 'checking' | 'waking' | 'ready' | 'unreachable';

export interface ServerWakeOptions {
  /** API origin without a trailing slash; empty for same origin. */
  baseUrl?: string;
  /** Must be stable across renders; a new function restarts the check. */
  fetchFn?: typeof fetch;
  /** Show the waking message after this long without an answer. */
  slowAfterMs?: number;
  /** Stop retrying after this long and report the server as unreachable. */
  giveUpAfterMs?: number;
  /** Longest wait for a single attempt; a cold start holds the request open. */
  attemptTimeoutMs?: number;
  /** Delay before the first retry; doubles up to 8 seconds. */
  firstRetryDelayMs?: number;
}

export interface ServerWake {
  status: ServerWakeStatus;
  elapsedSeconds: number;
  retry: () => void;
}

const maximumRetryDelayMs = 8_000;

export function useServerWake({
  baseUrl = '',
  fetchFn,
  slowAfterMs = 3_000,
  giveUpAfterMs = 90_000,
  attemptTimeoutMs = 30_000,
  firstRetryDelayMs = 1_000,
}: ServerWakeOptions = {}): ServerWake {
  const [status, setStatus] = useState<ServerWakeStatus>('checking');
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [run, setRun] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const startedAt = Date.now();
    const slowTimer = setTimeout(() => {
      setStatus((current) => (current === 'checking' ? 'waking' : current));
    }, slowAfterMs);
    const clock = setInterval(() => {
      setElapsedSeconds(Math.floor((Date.now() - startedAt) / 1_000));
    }, 1_000);
    const stopTimers = () => {
      clearTimeout(slowTimer);
      clearInterval(clock);
    };

    void waitForServer({
      url: `${baseUrl}/health/live`,
      fetchFn: fetchFn ?? ((input, init) => fetch(input, init)),
      signal: controller.signal,
      giveUpAfterMs,
      attemptTimeoutMs,
      firstRetryDelayMs,
    }).then((result) => {
      if (result === 'aborted') return;
      stopTimers();
      setStatus(result);
    });

    return () => {
      controller.abort();
      stopTimers();
    };
  }, [baseUrl, fetchFn, slowAfterMs, giveUpAfterMs, attemptTimeoutMs, firstRetryDelayMs, run]);

  const retry = useCallback(() => {
    setStatus('checking');
    setElapsedSeconds(0);
    setRun((current) => current + 1);
  }, []);

  return { status, elapsedSeconds, retry };
}

interface WaitOptions {
  url: string;
  fetchFn: typeof fetch;
  signal: AbortSignal;
  giveUpAfterMs: number;
  attemptTimeoutMs: number;
  firstRetryDelayMs: number;
}

async function waitForServer(options: WaitOptions): Promise<'ready' | 'unreachable' | 'aborted'> {
  const { url, fetchFn, signal, giveUpAfterMs, attemptTimeoutMs } = options;
  const startedAt = Date.now();
  let retryDelayMs = options.firstRetryDelayMs;

  for (;;) {
    const remainingMs = giveUpAfterMs - (Date.now() - startedAt);
    if (remainingMs <= 0) return 'unreachable';
    try {
      const response = await fetchFn(url, {
        cache: 'no-store',
        signal: AbortSignal.any([signal, AbortSignal.timeout(Math.min(attemptTimeoutMs, remainingMs))]),
      });
      if (response.ok) return 'ready';
    } catch {
      // Network error or attempt timeout: try again below.
    }
    if (signal.aborted) return 'aborted';

    // An abort ends the delay early; the next fetch then rejects without a
    // request and the check above returns.
    await delay(Math.min(retryDelayMs, Math.max(0, giveUpAfterMs - (Date.now() - startedAt))), signal);
    retryDelayMs = Math.min(retryDelayMs * 2, maximumRetryDelayMs);
  }
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timer);
        resolve();
      },
      { once: true },
    );
  });
}
