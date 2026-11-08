import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import text from '../components/ui/Text.module.css';
import { messages } from '../i18n';
import { useSessionState } from './AuthContext';

/** Shows its children to a signed-in user; sends everyone else to the sign-in page and back afterwards. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const state = useSessionState();
  const location = useLocation();

  if (state.status === 'unknown') {
    return (
      <p role="status" className={text.muted}>
        {messages.session.checking}
      </p>
    );
  }
  if (state.status === 'signed-out') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  return children;
}

/** Only same-site paths are followed after sign-in, never another origin. */
export function safeReturnPath(value: unknown): string {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/';
  }
  return value === '/login' ? '/' : value;
}
