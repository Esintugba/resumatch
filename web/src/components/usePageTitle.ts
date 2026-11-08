import { useEffect, useRef } from 'react';
import { messages } from '../i18n';

/** Sets the document title and moves focus to the page heading, so a screen reader announces the new page. */
export function usePageHeading<T extends HTMLElement>(title: string) {
  const heading = useRef<T>(null);
  useEffect(() => {
    document.title = messages.pageTitle(title);
    heading.current?.focus();
  }, [title]);
  return heading;
}
