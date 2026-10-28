import type { InstanceSnapshot } from '@/api/generated';
import { initialInstances, initialJobs } from '@/mocks/data';
import { tabStatus } from './useTabStatus';

const server = (state: InstanceSnapshot['state']) => ({ ...initialInstances()[0]!, state });

describe('tab status', () => {
  it('is red only while Core is unreachable', () => {
    expect(tabStatus('disconnected', [server('running')], [])).toBe('red');
    expect(tabStatus('connected', [server('crashed')], [])).toBe('gray');
  });

  it('is yellow while a job runs or a server starts', () => {
    expect(tabStatus('connected', [server('running')], initialJobs())).toBe('yellow');
    expect(tabStatus('connected', [server('starting')], [])).toBe('yellow');
  });

  it('is green when servers run and grey when none does', () => {
    expect(tabStatus('connected', [server('running'), server('stopped')], [])).toBe('green');
    expect(tabStatus('off', [server('stopped')], [])).toBe('gray');
  });
});
