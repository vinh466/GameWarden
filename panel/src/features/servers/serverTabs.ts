/** The server detail's tabs in their order (docs/11 §3, docs/07 §6); each is its own route. */
export const allServerTabs = [
  'overview',
  'console',
  'settings',
  'mods',
  'integration',
  'jobs',
  'info',
] as const;

export type ServerTab = (typeof allServerTabs)[number];

/**
 * The tabs a server shows: Mods only when its adapter has a mod loader (docs/13 §4.10), the game API only when it
 * has an integration (docs/16).
 */
export function serverTabs(
  modLoader: string | null | undefined,
  hasIntegration = false
): ServerTab[] {
  return allServerTabs.filter(
    (tab) => (tab !== 'mods' || !!modLoader) && (tab !== 'integration' || hasIntegration)
  );
}

/** The tab a path is on; the overview is the server's own path. A tab may have child routes (Mods → Config). */
export function tabOfPath(pathname: string): ServerTab {
  const segment = pathname.split('/').filter(Boolean)[2];
  return allServerTabs.find((tab) => tab !== 'overview' && tab === segment) ?? 'overview';
}
