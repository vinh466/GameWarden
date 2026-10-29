import { useCallback, useMemo, useState, type ReactNode } from 'react';
import { HelpDrawer } from './HelpDrawer';
import type { HelpRequest } from './types';
import { HelpContext } from './useHelp';

/**
 * Mounted once in App.tsx: the help drawer and `useHelp()` for every help button (docs/12 §4). Closing the drawer
 * returns the focus to the button that opened it (Mantine's `returnFocus`), also inside a host dialog.
 */
export function HelpProvider({ children }: { children: ReactNode }) {
  const [opened, setOpened] = useState(false);
  const [history, setHistory] = useState<HelpRequest[]>([]);

  const open = useCallback((request: HelpRequest) => {
    setHistory([request]);
    setOpened(true);
  }, []);
  const close = useCallback(() => setOpened(false), []);

  const value = useMemo(() => ({ open, close, opened }), [open, close, opened]);
  return (
    <HelpContext value={value}>
      {children}
      <HelpDrawer opened={opened} history={history} onClose={close} onNavigate={setHistory} />
    </HelpContext>
  );
}
