import { createContext, use } from 'react';
import type { HelpRequest } from './types';

export interface HelpContextValue {
  /** Opens the one help drawer with this topic; replaces whatever it showed. */
  open: (request: HelpRequest) => void;
  close: () => void;
  opened: boolean;
}

export const HelpContext = createContext<HelpContextValue | null>(null);

/** The panel's help drawer (docs/12 §4); `HelpProvider` in App.tsx provides it. */
export function useHelp(): HelpContextValue {
  const help = use(HelpContext);
  if (!help) {
    throw new Error('useHelp needs a HelpProvider (App.tsx mounts one).');
  }

  return help;
}

/**
 * Props for a Modal or Drawer that contains help buttons (docs/12 §4): while the help drawer is open over it, the
 * host does not close on Escape, so Escape closes only the help drawer. The host keeps its focus trap: in Mantine
 * 9.7 a trap handles Tab only while the focus is inside its own node, so it leaves the help drawer's Tab alone, and
 * the host's own focus return (to the button that opened it) stays intact.
 */
export function useHelpHost(): { closeOnEscape: boolean } {
  const { opened } = useHelp();
  return { closeOnEscape: !opened };
}
