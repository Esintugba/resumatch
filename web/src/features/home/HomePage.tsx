import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { useSession, useSessionState } from '../../auth/AuthContext';
import type { CurrentUser } from '../../auth/session';
import { usePageHeading } from '../../components/usePageTitle';
import { messages } from '../../i18n';

const t = messages.home;

/** The protected start page. Empty until the resume screens arrive. */
export function HomePage() {
  const session = useSession();
  const state = useSessionState();
  const navigate = useNavigate();
  const heading = usePageHeading<HTMLHeadingElement>(t.title);
  const [profile, setProfile] = useState<CurrentUser | null>(null);
  const [signingOut, setSigningOut] = useState(false);

  // Goes through authorizedFetch, so an expired access token is refreshed once and the call retried.
  useEffect(() => {
    const controller = new AbortController();
    session
      .authorizedFetch('/api/auth/me', { signal: controller.signal })
      .then(async (response) => {
        if (response.ok) setProfile((await response.json()) as CurrentUser);
      })
      .catch(() => {
        // The greeting falls back to the sign-in response.
      });
    return () => {
      controller.abort();
    };
  }, [session]);

  const user = profile ?? (state.status === 'signed-in' ? state.user : null);
  const firstName = user?.firstName;

  async function signOut() {
    if (signingOut) return;
    setSigningOut(true);
    await session.logout();
    await navigate('/login', { replace: true });
  }

  return (
    <section className="card">
      <h1 ref={heading} tabIndex={-1}>
        {firstName ? t.greeting(firstName) : t.greetingFallback}
      </h1>
      <p className="muted">{t.empty}</p>
      <button type="button" className="secondary" disabled={signingOut} onClick={() => void signOut()}>
        {signingOut ? t.signingOut : t.signOut}
      </button>
    </section>
  );
}
