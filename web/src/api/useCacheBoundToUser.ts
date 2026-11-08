import type { QueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import type { AuthSession, SessionState } from '../auth/session';

/**
 * Cached API data belongs to one user. It is dropped when the session ends
 * (sign-out here or in another tab, or a failed refresh) and when a different
 * user signs in, synchronously in the session listener, so nothing can render
 * another user's cached data in between.
 */
export function useCacheBoundToUser(session: AuthSession, client: QueryClient): void {
  useEffect(() => {
    let cachedFor: string | null = null;
    const onChange = (state: SessionState) => {
      if (state.status === 'unknown') return;
      const user = state.status === 'signed-in' ? (state.user.id ?? '') : null;
      if (user === null || user !== cachedFor) client.clear();
      cachedFor = user;
    };
    onChange(session.getState());
    return session.subscribe(onChange);
  }, [session, client]);
}
