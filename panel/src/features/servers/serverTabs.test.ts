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

  it('show the game API only for an adapter with an integration (docs/16)', () => {
    expect(serverTabs(null, false)).not.toContain('integration');
    expect(serverTabs(null, true)).toEqual([
      'overview',
      'console',
      'settings',
      'integration',
      'jobs',
      'info',
    ]);
    expect(serverTabs('bepinex', true)).toContain('integration');
  });

  it('know the tab of a path', () => {
    expect(tabOfPath('/servers/valheim-main')).toBe('overview');
    expect(tabOfPath('/servers/valheim-main/mods')).toBe('mods');
    expect(tabOfPath('/servers/valheim-main/mods/config')).toBe('mods');
    expect(tabOfPath('/servers/valheim-main/mods/log')).toBe('mods');
    expect(tabOfPath('/servers/valheim-main/settings')).toBe('settings');
    expect(tabOfPath('/servers/valheim-main/integration')).toBe('integration');
  });
});
