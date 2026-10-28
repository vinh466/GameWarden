import { initialInstances, initialJobs, system } from '@/mocks/data';
import { attentionItems } from './attention';

describe('attentionItems', () => {
  it('lists the server stopped with a problem and the job that failed today', () => {
    const items = attentionItems(initialInstances(), initialJobs(), system);
    expect(items.map((item) => item.kind)).toEqual(['server', 'job']);
  });

  it('ignores failures older than 24 hours', () => {
    const tomorrow = Date.now() + 24 * 60 * 60 * 1000;
    const kinds = attentionItems(initialInstances(), initialJobs(), system, tomorrow).map(
      (item) => item.kind
    );
    expect(kinds).not.toContain('job');
  });

  it('lists missing prerequisites that an adapter needs, and blocked hosts', () => {
    const items = attentionItems([], [], {
      ...system,
      prerequisites: [
        { id: 'vcredist-x64', installed: false, neededBy: ['valheim'], hint: '' },
        { id: 'directx-legacy', installed: false, neededBy: [], hint: '' },
      ],
      network: [
        { host: 'steamcdn-a.akamaihd.net', purpose: 'SteamCMD', status: 'blocked', detail: null },
      ],
    });
    expect(items).toEqual([
      { kind: 'prerequisite', id: 'vcredist-x64', neededBy: ['valheim'] },
      { kind: 'dns', host: 'steamcdn-a.akamaihd.net', purpose: 'SteamCMD', status: 'blocked' },
    ]);
  });
});
