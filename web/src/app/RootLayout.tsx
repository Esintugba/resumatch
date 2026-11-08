import { NavLink, Outlet } from 'react-router';
import { useSession, useSessionState } from '../auth/AuthContext';
import { Button } from '../components/ui/Button';
import { cx } from '../components/ui/cx';
import { Frame } from '../components/ui/Frame';
import { messages } from '../i18n';
import styles from './RootLayout.module.css';

/** The frame for every route; signed-in users get the navigation and sign-out. */
export function RootLayout() {
  const session = useSession();
  const state = useSessionState();

  const headerEnd =
    state.status === 'signed-in' ? (
      <nav aria-label={messages.nav.label} className={styles.nav}>
        <ul className={styles.navList}>
          <li>
            <NavLink to="/app/resumes" end={false} className={cx(styles.navLink)}>
              {messages.nav.resumes}
            </NavLink>
          </li>
        </ul>
        <span className={styles.user}>
          {messages.nav.signedInAs(`${state.user.firstName ?? ''} ${state.user.lastName ?? ''}`.trim())}
        </span>
        {/* The guard sends the signed-out user to /login. */}
        <Button tone="secondary" onClick={() => void session.logout()}>
          {messages.nav.signOut}
        </Button>
      </nav>
    ) : null;

  return (
    <Frame headerEnd={headerEnd}>
      <Outlet />
    </Frame>
  );
}
