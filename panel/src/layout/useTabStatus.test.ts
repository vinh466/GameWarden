import type { InstanceSnapshot } from '@/api/generated';
import { initialInstances, initialJobs } from '@/mocks/data';
import { tabStatus } from './useTabStatus';

const server = (state: InstanceSnapshot['state']) => ({ ...initialInstances()[0]!, state });

describe('tab status', () => {
  it('is red when something needs attention or Core is unreachable', () => {
    expect(tabStatus(1, 'connected', [server('running')], [])).toBe('red');
    expect(tabStatus(0, 'disconnected', [server('running')], [])).toBe('red');
  });

  it('is yellow while a job runs or a server starts', () => {
    expect(tabStatus(0, 'connected', [server('running')], initialJobs())).toBe('yellow');
    expect(tabStatus(0, 'connected', [server('starting')], [])).toBe('yellow');
  });

  it('is green when servers run and grey when none does', () => {
    expect(tabStatus(0, 'connected', [server('running'), server('stopped')], [])).toBe('green');
    expect(tabStatus(0, 'off', [server('stopped')], [])).toBe('gray');
  });
});
