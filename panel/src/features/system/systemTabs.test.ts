import { describe, expect, it } from 'vitest';
import { systemTabOfPath, systemTabPaths, systemTabs } from './systemTabs';

describe('systemTabs', () => {
  it('maps every tab to a unique path', () => {
    const paths = systemTabs.map((tab) => systemTabPaths[tab]);
    expect(new Set(paths).size).toBe(systemTabs.length);
  });

  it('finds the tab of a path', () => {
    expect(systemTabOfPath('/system')).toBe('overview');
    expect(systemTabOfPath('/system/')).toBe('overview');
    expect(systemTabOfPath('/system/jobs')).toBe('jobs');
    expect(systemTabOfPath('/system/activity')).toBe('activity');
    expect(systemTabOfPath('/system/adapters')).toBe('adapters');
    expect(systemTabOfPath('/system/agents')).toBe('agents');
    expect(systemTabOfPath('/system/logs')).toBe('logs');
    expect(systemTabOfPath('/system/notifications')).toBe('notifications');
  });

  it('keeps a tab selected on a child route', () => {
    expect(systemTabOfPath('/system/logs/42')).toBe('logs');
  });

  it('falls back to the overview for an unknown path', () => {
    expect(systemTabOfPath('/system/unknown')).toBe('overview');
    expect(systemTabOfPath('/servers')).toBe('overview');
  });
});
