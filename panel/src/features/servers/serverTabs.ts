/** The server detail's tabs in their order (docs/11 §3, docs/07 §6); each is its own route. */
export const allServerTabs = ['overview', 'console', 'settings', 'mods', 'jobs', 'info'] as const;

export type ServerTab = (typeof allServerTabs)[number];

/** The tabs a server shows: Mods only when its adapter has a mod loader (docs/13 §4.10). */
export function serverTabs(modLoader: string | null | undefined): ServerTab[] {
  return allServerTabs.filter((tab) => tab !== 'mods' || !!modLoader);
}

/** The tab a path is on; the overview is the server's own path. */
export function tabOfPath(pathname: string): ServerTab {
  return (
    allServerTabs.find((tab) => tab !== 'overview' && pathname.endsWith(`/${tab}`)) ?? 'overview'
  );
}
