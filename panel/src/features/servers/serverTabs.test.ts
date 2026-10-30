import { serverTabs, tabOfPath } from './serverTabs';

describe('server tabs', () => {
  it('show Mods only for an adapter with a mod loader (docs/13 §4.10)', () => {
    expect(serverTabs(null)).toEqual(['overview', 'console', 'settings', 'jobs', 'info']);
    expect(serverTabs(undefined)).not.toContain('mods');
    expect(serverTabs('bepinex')).toEqual([
      'overview',
      'console',
      'settings',
      'mods',
      'jobs',
      'info',
    ]);
  });

  it('know the tab of a path', () => {
    expect(tabOfPath('/servers/valheim-main')).toBe('overview');
    expect(tabOfPath('/servers/valheim-main/mods')).toBe('mods');
    expect(tabOfPath('/servers/valheim-main/settings')).toBe('settings');
  });
});
