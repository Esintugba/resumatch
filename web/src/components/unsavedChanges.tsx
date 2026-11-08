import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';

interface UnsavedChanges {
  dirty: boolean;
  setDirty: (dirty: boolean) => void;
}

const UnsavedChangesContext = createContext<UnsavedChanges>({
  dirty: false,
  setDirty: () => undefined,
});

/** Lets the page frame know a form has unsaved changes, so sign-out can ask first. */
export function UnsavedChangesProvider({ children }: { children: ReactNode }) {
  const [dirty, setDirty] = useState(false);
  const value = useMemo(() => ({ dirty, setDirty }), [dirty]);
  return <UnsavedChangesContext.Provider value={value}>{children}</UnsavedChangesContext.Provider>;
}

export function useUnsavedChanges(): boolean {
  return useContext(UnsavedChangesContext).dirty;
}

/** Reports a page's unsaved changes while it is mounted. */
export function useReportUnsavedChanges(dirty: boolean): void {
  const { setDirty } = useContext(UnsavedChangesContext);
  useEffect(() => {
    setDirty(dirty);
    return () => {
      setDirty(false);
    };
  }, [dirty, setDirty]);
}
