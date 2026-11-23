/** The System screen's peer views (docs/11 §4); each is its own route, so a tab can be linked and reloaded. */
export const systemTabs = [
  'overview',
  'jobs',
  'logs',
  'notifications',
  'agents',
  'operations',
  'adapters',
] as const;

export type SystemTab = (typeof systemTabs)[number];

export const systemTabPaths = {
  overview: '/system',
  jobs: '/system/jobs',
  logs: '/system/logs',
  notifications: '/system/notifications',
  agents: '/system/agents',
  operations: '/system/operations',
  adapters: '/system/adapters',
} as const satisfies Record<SystemTab, string>;

/** The tab a path is on; a child route such as a future log detail keeps its tab selected. */
export function systemTabOfPath(pathname: string): SystemTab {
  const segment = pathname.split('/').filter(Boolean)[1];
  return systemTabs.find((tab) => tab !== 'overview' && tab === segment) ?? 'overview';
}
