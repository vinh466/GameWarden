import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { HelpDrawer } from './HelpDrawer';
import type { HelpRequest } from './types';
import { HelpContext } from './useHelp';

/**
 * Mounted once in App.tsx: the help drawer and `useHelp()` for every help button (docs/12 §4).
 *
 * Focus goes back to the button that opened the drawer. Mantine's `returnFocus` does that on a page; inside a host
 * dialog, the host's focus trap comes back on when the drawer closes and focuses the host's first field first, so
 * the button is focused again right after that.
 */
export function HelpProvider({ children }: { children: ReactNode }) {
  const [opened, setOpened] = useState(false);
  const [history, setHistory] = useState<HelpRequest[]>([]);
  const trigger = useRef<HTMLElement | null>(null);
  const wasOpened = useRef(false);

  const open = useCallback((request: HelpRequest) => {
    trigger.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setHistory([request]);
    setOpened(true);
  }, []);
  const close = useCallback(() => setOpened(false), []);

  useEffect(() => {
    if (opened || !wasOpened.current) {
      wasOpened.current = opened;
      return undefined;
    }

    wasOpened.current = false;
    const button = trigger.current;
    // A macrotask after the host trap's own (scheduled earlier in this commit, by a child of this provider).
    const timeout = window.setTimeout(() => {
      if (button?.isConnected) {
        button.focus({ preventScroll: true });
      }
    });
    return () => window.clearTimeout(timeout);
  }, [opened]);

  const value = useMemo(() => ({ open, close, opened }), [open, close, opened]);
  return (
    <HelpContext value={value}>
      {children}
      <HelpDrawer opened={opened} history={history} onClose={close} onNavigate={setHistory} />
    </HelpContext>
  );
}
