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
 * host neither traps focus nor closes on Escape, as `Drawer.Stack` does for the drawers below the active one.
 */
export function useHelpHost(): { trapFocus: boolean; closeOnEscape: boolean } {
  const { opened } = useHelp();
  return { trapFocus: !opened, closeOnEscape: !opened };
}
