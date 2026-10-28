import { initialActivity } from '@/mocks/data';
import { activityGroup } from './activityKinds';

describe('activity types', () => {
  it('groups lines the way the filter offers them', () => {
    const groups = Object.fromEntries(
      initialActivity().map((item) => [item.kind, activityGroup(item)])
    );

    expect(groups['instance.crashed']).toBe('servers');
    expect(groups['job.failed']).toBe('jobs');
    expect(groups['command']).toBe('commands');
    expect(groups['core.started']).toBe('system');
  });
});
