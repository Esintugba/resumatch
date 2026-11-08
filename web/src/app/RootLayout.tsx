import { useState } from 'react';
import { NavLink, Outlet } from 'react-router';
import { useSession, useSessionState } from '../auth/AuthContext';
import { UnsavedChangesProvider, useUnsavedChanges } from '../components/unsavedChanges';
import { Button } from '../components/ui/Button';
import { ConfirmDialog } from '../components/ui/ConfirmDialog';
import { cx } from '../components/ui/cx';
import { Frame } from '../components/ui/Frame';
import { messages } from '../i18n';
import styles from './RootLayout.module.css';

/** The frame for every route; signed-in users get the navigation and sign-out. */
export function RootLayout() {
  return (
    <UnsavedChangesProvider>
      <Layout />
    </UnsavedChangesProvider>
  );
}

function Layout() {
  const session = useSession();
  const state = useSessionState();
  const dirty = useUnsavedChanges();
  const [confirmingSignOut, setConfirmingSignOut] = useState(false);

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
        <Button
          tone="secondary"
          onClick={() => {
            if (dirty) setConfirmingSignOut(true);
            else void session.logout();
          }}
        >
          {messages.nav.signOut}
        </Button>
      </nav>
    ) : null;

  return (
    <Frame headerEnd={headerEnd}>
      <Outlet />
      {confirmingSignOut && (
        <ConfirmDialog
          title={messages.unsaved.title}
          description={messages.unsaved.signOutDescription}
          confirmLabel={messages.unsaved.signOutConfirm}
          cancelLabel={messages.unsaved.signOutStay}
          onCancel={() => {
            setConfirmingSignOut(false);
          }}
          onConfirm={() => {
            setConfirmingSignOut(false);
            void session.logout();
          }}
        />
      )}
    </Frame>
  );
}
