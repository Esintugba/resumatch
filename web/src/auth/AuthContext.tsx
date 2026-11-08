import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from 'react';
import type { AuthSession, SessionState } from './session';

const SessionContext = createContext<AuthSession | null>(null);

/** Provides the session and checks once, on page load, whether the browser still has one. */
export function AuthProvider({ session, children }: { session: AuthSession; children: ReactNode }) {
  useEffect(() => {
    void session.restore();
  }, [session]);

  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}

export function useSession(): AuthSession {
  const session = useContext(SessionContext);
  if (!session) throw new Error('useSession must be used inside AuthProvider.');
  return session;
}

export function useSessionState(): SessionState {
  const session = useSession();
  return useSyncExternalStore(
    (listener) => session.subscribe(listener),
    () => session.getState(),
  );
}
