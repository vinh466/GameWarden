import i18n from '@/i18n';
import { initialInstances, initialJobs } from '@/mocks/data';
import type { InstanceSnapshot, JobInfo } from './generated';
import { noticeForCore, noticeForInstance, noticeForJob } from './realtimeNotices';

const t = i18n.getFixedT('en');

describe('realtime notices', () => {
  it('toasts a job once, when it finishes', () => {
    const running = { ...initialJobs()[0]!, state: 'running', finishedAt: null } as JobInfo;
    const failed = {
      ...running,
      state: 'failed',
      finishedAt: '2026-10-28T10:00:00Z',
      message: 'no connection',
    } as JobInfo;

    expect(noticeForJob(t, undefined, running, 'Test world')).toBeNull();
    expect(noticeForJob(t, running, failed, 'Test world')).toEqual({
      color: 'red',
      message: 'Install of Test world failed',
      detail: 'no connection',
    });
    expect(noticeForJob(t, failed, failed, 'Test world')).toBeNull();
  });

  it('toasts a crash, not every state change', () => {
    const running = { ...initialInstances()[0]!, state: 'running' } as InstanceSnapshot;
    const crashed = {
      ...running,
      state: 'crashed',
      lastProblem: 'exited with code 3',
    } as InstanceSnapshot;

    expect(noticeForInstance(t, running, crashed)?.message).toBe('Vikings of the North crashed');
    expect(noticeForInstance(t, running, { ...running, state: 'stopping' })).toBeNull();
    expect(noticeForInstance(t, undefined, crashed)).toBeNull();
  });

  it('notices an update and a restart of Core from its heartbeat', () => {
    expect(noticeForCore(t, null, { version: '0.2.0', uptimeSec: 5 })).toBeNull();
    expect(
      noticeForCore(
        t,
        { version: '0.2.0-rc.1', uptimeSec: 900 },
        { version: '0.2.0', uptimeSec: 5 }
      )?.message
    ).toBe('GameWarden is now 0.2.0');
    expect(
      noticeForCore(t, { version: '0.2.0', uptimeSec: 900 }, { version: '0.2.0', uptimeSec: 5 })
        ?.color
    ).toBe('orange');
    expect(
      noticeForCore(t, { version: '0.2.0', uptimeSec: 900 }, { version: '0.2.0', uptimeSec: 915 })
    ).toBeNull();
  });
});
