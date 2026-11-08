import { useEffect } from 'react';
import { useBlocker, type Blocker } from 'react-router';

/**
 * While there are unsaved changes: asks before leaving the page inside the
 * app (router blocker) and lets the browser ask before closing or reloading
 * the tab (beforeunload). Returns the blocker so the page can show its dialog.
 */
export function useUnsavedChangesGuard(dirty: boolean): Blocker {
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (!dirty) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      // preventDefault is the standard way to ask the browser to confirm.
      event.preventDefault();
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => {
      window.removeEventListener('beforeunload', onBeforeUnload);
    };
  }, [dirty]);

  // A blocked navigation that is no longer needed (changes saved) goes ahead.
  useEffect(() => {
    if (blocker.state === 'blocked' && !dirty) blocker.reset();
  }, [blocker, dirty]);

  return blocker;
}
